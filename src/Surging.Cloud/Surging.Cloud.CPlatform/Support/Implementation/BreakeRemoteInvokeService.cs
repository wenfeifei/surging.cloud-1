﻿using Microsoft.Extensions.Logging;
using Surging.Cloud.CPlatform.Messages;
using Surging.Cloud.CPlatform.Runtime.Client;
using Surging.Cloud.CPlatform.Runtime.Client.Address.Resolvers.Implementation.Selectors.Implementation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Surging.Cloud.CPlatform.HashAlgorithms;
using Surging.Cloud.CPlatform.Transport.Implementation;
using Surging.Cloud.CPlatform.Filters;
using Autofac;
using System.Threading;
using Surging.Cloud.CPlatform.Filters.Implementation;
using System.Runtime.CompilerServices;
using Surging.Cloud.CPlatform.Exceptions;

namespace Surging.Cloud.CPlatform.Support.Implementation
{
    public class BreakeRemoteInvokeService : IBreakeRemoteInvokeService
    {
        private readonly IServiceCommandProvider _commandProvider;
        private readonly IRemoteInvokeService _remoteInvokeService;
        private readonly ILogger<BreakeRemoteInvokeService> _logger;
        private readonly ConcurrentDictionary<string, ServiceInvokeListenInfo> _serviceInvokeListenInfo = new ConcurrentDictionary<string, ServiceInvokeListenInfo>();
        private readonly IHashAlgorithm _hashAlgorithm;
        private readonly IEnumerable<IExceptionFilter> exceptionFilters = new List<IExceptionFilter>();

        public BreakeRemoteInvokeService(IHashAlgorithm hashAlgorithm, IServiceCommandProvider commandProvider, ILogger<BreakeRemoteInvokeService> logger,
            IRemoteInvokeService remoteInvokeService,
             CPlatformContainer serviceProvider)
        {
            _commandProvider = commandProvider;
            _remoteInvokeService = remoteInvokeService;
            _logger = logger;
            _hashAlgorithm = hashAlgorithm;
            if (serviceProvider.Current.IsRegistered<IExceptionFilter>())
                exceptionFilters = serviceProvider.GetInstances<IEnumerable<IExceptionFilter>>();
        }

        public async Task<RemoteInvokeResultMessage> InvokeAsync(IDictionary<string, object> parameters, string serviceId, string serviceKey, bool decodeJOject,bool isFailoverInvoke = false)
        {
            var serviceInvokeInfos = _serviceInvokeListenInfo.GetOrAdd(serviceId,
                new ServiceInvokeListenInfo()
                {
                    FirstInvokeTime = DateTime.Now,
                    FinalRemoteInvokeTime = DateTime.Now
                });
            UpdateAttachments(parameters);
            RpcContext.GetContext().SetAttachment("isFailoverInvoke", isFailoverInvoke);
            var command = await _commandProvider.GetCommand(serviceId);
            var intervalSeconds = (DateTime.Now - serviceInvokeInfos.FinalRemoteInvokeTime).TotalSeconds;
            bool reachConcurrentRequest() => serviceInvokeInfos.ConcurrentRequests > command.MaxConcurrentRequests;
            bool reachRequestVolumeThreshold() => intervalSeconds <= 10 && serviceInvokeInfos.SinceFaultRemoteServiceRequests > command.BreakerRequestVolumeThreshold;
            bool reachErrorThresholdPercentage() 
            {
                var errorThresholdPercent = serviceInvokeInfos.FaultRemoteServiceRequests / (serviceInvokeInfos.RemoteServiceRequests ?? 1) * 100;
                return errorThresholdPercent > command.BreakeErrorThresholdPercentage && serviceInvokeInfos.RemoteServiceRequests.HasValue && serviceInvokeInfos.RemoteServiceRequests.Value > 1;
            }

            var item = GetHashItem(command, parameters);
            if (command.BreakerForceClosed)
            {
                _serviceInvokeListenInfo.AddOrUpdate(serviceId, new ServiceInvokeListenInfo(), (k, v) => { v.LocalServiceRequests++; return v; });
                return await MonitorRemoteInvokeAsync(parameters, serviceId, serviceKey, decodeJOject, command.ExecutionTimeoutInMilliseconds, item);

            }
            else
            {
                if (reachConcurrentRequest() || reachRequestVolumeThreshold() || reachErrorThresholdPercentage())
                {
                    if (intervalSeconds * 1000 > command.BreakeSleepWindowInMilliseconds || isFailoverInvoke)
                    {
                        return await MonitorRemoteInvokeAsync(parameters, serviceId, serviceKey, decodeJOject, command.ExecutionTimeoutInMilliseconds, item);
                    }
                    else
                    {
                        _serviceInvokeListenInfo.AddOrUpdate(serviceId, new ServiceInvokeListenInfo(), (k, v) => { v.LocalServiceRequests++; return v; });
                        if (_logger.IsEnabled(LogLevel.Debug))
                        {
                            _logger.LogDebug($"当前服务{serviceId}-{serviceKey}当前不可用,请稍后重试");
                        }
                        var breakeSeconds = Math.Round((command.BreakeSleepWindowInMilliseconds - intervalSeconds * 1000) / 1000, 0);
                        return new RemoteInvokeResultMessage() { ExceptionMessage = $"当前没有可用的服务{serviceId}-{serviceKey},请稍后{breakeSeconds}s后重试", StatusCode = StatusCode.ServiceUnavailability };                 
                    }
                }
                else
                {
                    return await MonitorRemoteInvokeAsync(parameters, serviceId, serviceKey, decodeJOject, command.ExecutionTimeoutInMilliseconds, item);
                }
            }
        }

        private async Task<RemoteInvokeResultMessage> MonitorRemoteInvokeAsync(IDictionary<string, object> parameters, string serviceId, string serviceKey, bool decodeJOject, int requestTimeout, string item)
        {
            CancellationTokenSource source = new CancellationTokenSource();
            var token = source.Token;
            var invokeMessage = new RemoteInvokeMessage
            {
                Parameters = parameters,
                ServiceId = serviceId,
                ServiceKey = serviceKey,
                DecodeJOject = decodeJOject,
                Attachments = RpcContext.GetContext().GetContextParameters()
            };
            try
            {
                _serviceInvokeListenInfo.AddOrUpdate(serviceId, new ServiceInvokeListenInfo(), (k, v) =>
                {
                    v.RemoteServiceRequests = v.RemoteServiceRequests == null ? 1 : ++v.RemoteServiceRequests;
                    v.FinalRemoteInvokeTime = DateTime.Now;
                    ++v.ConcurrentRequests;
                    return v;
                });
                var message = await _remoteInvokeService.InvokeAsync(new RemoteInvokeContext
                {
                    Item = item,
                    InvokeMessage = invokeMessage
                }, requestTimeout);
                _serviceInvokeListenInfo.AddOrUpdate(serviceId, new ServiceInvokeListenInfo(), (k, v) =>
                {
                    v.SinceFaultRemoteServiceRequests = 0;
                    --v.ConcurrentRequests; return v;
                });
                return message;
            }
            catch (Exception ex)
            {
                
                await ExecuteExceptionFilter(ex, invokeMessage, token);
                if ((ex.InnerException != null && ex.InnerException is BusinessException) || ex is BusinessException)
                {
                    _serviceInvokeListenInfo.AddOrUpdate(serviceId, new ServiceInvokeListenInfo(), (k, v) =>
                    {
                        v.SinceFaultRemoteServiceRequests = 0;
                        --v.ConcurrentRequests;
                        return v;
                    });
                    return new RemoteInvokeResultMessage()
                    {
                        ExceptionMessage = ex.InnerException.GetExceptionMessage(),
                        Result = null,
                        StatusCode = ex.InnerException.GetExceptionStatusCode()
                    };
                }
                else 
                {
                    _serviceInvokeListenInfo.AddOrUpdate(serviceId, new ServiceInvokeListenInfo(), (k, v) =>
                    {
                        ++v.FaultRemoteServiceRequests;
                        ++v.SinceFaultRemoteServiceRequests;
                        --v.ConcurrentRequests;                        
                        return v;
                    });
                }

                return new RemoteInvokeResultMessage()
                {
                    ExceptionMessage = ex.GetExceptionMessage(),
                    Result = null,
                    StatusCode = ex.GetExceptionStatusCode()
                };
            }
        }


        private async Task ExecuteExceptionFilter(Exception ex, RemoteInvokeMessage invokeMessage, CancellationToken token)
        {
            foreach (var filter in exceptionFilters)
            {
                await filter.ExecuteExceptionFilterAsync(new RpcActionExecutedContext
                {
                    Exception = ex,
                    InvokeMessage = invokeMessage
                }, token);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetHashItem(ServiceCommand command, IDictionary<string, object> parameters)
        {
            string result = "";
            if (command.ShuntStrategy == AddressSelectorMode.HashAlgorithm)
            {
                var parameter = parameters.Values.FirstOrDefault();
                result = parameter?.ToString();
            }
            return result;
        }

        private void UpdateAttachments(IDictionary<string, object> parameters)
        {
           
            if (parameters.ContainsKey("attachments"))
            {
                var attachments = parameters["attachments"] as Dictionary<string, object>;
                if (attachments != null)
                {
                    foreach (var attachment in attachments)
                    {
                        RpcContext.GetContext().SetAttachment(attachment.Key, attachment.Value);
                    }
                }
                parameters.Remove("attachments");
            }
        }
    }
}
