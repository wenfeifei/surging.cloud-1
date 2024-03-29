﻿using Surging.Cloud.ApiGateWay.OAuth;
using Surging.Cloud.CPlatform;
using Surging.Cloud.CPlatform.Filters.Implementation;
using Surging.Cloud.CPlatform.Messages;
using Surging.Cloud.CPlatform.Transport.Implementation;
using Surging.Cloud.CPlatform.Utilities;
using Surging.Cloud.KestrelHttpServer.Filters;
using Surging.Cloud.KestrelHttpServer.Filters.Implementation;
using System.Threading.Tasks;
using Autofac;
using System;
using System.Collections.Concurrent;
using Surging.Cloud.ProxyGenerator;
using System.Collections.Generic;
using Surging.Cloud.CPlatform.Routing;
using System.Linq;
using System.Security.Claims;
using Surging.Cloud.CPlatform.Exceptions;
using SurgingClaimTypes = Surging.Cloud.CPlatform.ClaimTypes;
using Surging.Cloud.CPlatform.Runtime.Server.Implementation.ServiceDiscovery.Attributes;
using Surging.Cloud.CPlatform.Runtime;

namespace Surging.Cloud.Stage.Filters
{
    public class AuthorizationFilterAttribute : IAuthorizationFilter
    {
        private readonly IAuthorizationServerProvider _authorizationServerProvider;
        private readonly IServiceProxyProvider _serviceProxyProvider;
        private readonly IServiceRouteProvider _serviceRouteProvider;
        private const int _order = int.MaxValue;
        public AuthorizationFilterAttribute()
        {
            _authorizationServerProvider = ServiceLocator.Current.Resolve<IAuthorizationServerProvider>();
            _serviceProxyProvider = ServiceLocator.Current.Resolve<IServiceProxyProvider>();
            _serviceRouteProvider = ServiceLocator.Current.Resolve<IServiceRouteProvider>();
        }

        public int Order { get { return _order; } }

        public async Task OnAuthorization(AuthorizationFilterContext filterContext)
        {
            var gatewayAppConfig = AppConfig.Options.ApiGetWay;

            if (filterContext.Route != null && filterContext.Route.ServiceDescriptor.DisableNetwork())
            {
                var actionName = filterContext.Route.ServiceDescriptor.GroupName().IsNullOrEmpty() ? filterContext.Route.ServiceDescriptor.RoutePath : filterContext.Route.ServiceDescriptor.GroupName();
                filterContext.Result = new HttpResultMessage<object> { IsSucceed = false, StatusCode = CPlatform.Exceptions.StatusCode.UnAuthorized, Message = $"{actionName}禁止被外网访问" };
            }
            else
            {
                var token = filterContext.Context.Request.Headers["Authorization"];

                if (filterContext.Route != null)
                {
                    if (filterContext.Route.ServiceDescriptor.AuthType() == AuthorizationType.JWT.ToString())
                    {

                        if (token.Any() && token.Count >= 1)
                        {
                            var validateResult = _authorizationServerProvider.ValidateClientAuthentication(token);
                            if (filterContext.Route.ServiceDescriptor.EnableAuthorization() && validateResult != ValidateResult.Success)
                            {
                                if (validateResult == ValidateResult.TokenFormatError)
                                {
                                    filterContext.Result = new HttpResultMessage<object> { IsSucceed = false, StatusCode = CPlatform.Exceptions.StatusCode.UnAuthentication, Message = "token格式不正确" };
                                    return;
                                }

                                if (validateResult == ValidateResult.SignatureError)
                                {
                                    filterContext.Result = new HttpResultMessage<object> { IsSucceed = false, StatusCode = CPlatform.Exceptions.StatusCode.UnAuthentication, Message = "token凭证不合法,请重新登录" };
                                    return;
                                }
                                if (validateResult == ValidateResult.TokenExpired)
                                {
                                    filterContext.Result = new HttpResultMessage<object> { IsSucceed = false, StatusCode = CPlatform.Exceptions.StatusCode.TokenExpired, Message = "登录超时,请重新登录" };
                                    return;
                                }
                            }

                            var payload = _authorizationServerProvider.GetPayload(token);

                            var claimsIdentity = new ClaimsIdentity();

                            foreach (var item in payload)
                            {
                                claimsIdentity.AddClaim(new Claim(item.Key, item.Value.ToString()));
                            }
                            
                            if (!gatewayAppConfig.AuthorizationRoutePath.IsNullOrEmpty() && filterContext.Route.ServiceDescriptor.EnableAuthorization())
                            {
                                var rpcParams = new Dictionary<string, object>() {
                                        {  "serviceId", filterContext.Route.ServiceDescriptor.Id }
                                    };
                                var authorizationRoutePath = await _serviceRouteProvider.GetRouteByPathOrRegexPath(gatewayAppConfig.AuthorizationRoutePath, HttpMethod.POST.ToString());
                                if (authorizationRoutePath == null)
                                {
                                    filterContext.Result = new HttpResultMessage<object> { IsSucceed = false, StatusCode = CPlatform.Exceptions.StatusCode.RequestError, Message = "没有找到实现接口鉴权的WebApi的路由信息" };
                                    return;
                                }
                                var attachments = new Dictionary<string, object>();
                                foreach (var kv in payload)
                                {
                                    attachments.TryAdd(kv.Key,kv.Value);
                                }
                                rpcParams.Add("attachments", attachments);
                                             var checkPermissionResult = await _serviceProxyProvider.Invoke<IDictionary<string,object>>(rpcParams, gatewayAppConfig.AuthorizationRoutePath, HttpMethod.POST, gatewayAppConfig.AuthorizationServiceKey);
                                    if (checkPermissionResult == null || !checkPermissionResult.ContainsKey("isPermission"))
                                    {
                                        filterContext.Result = new HttpResultMessage<object> { IsSucceed = false, StatusCode = StatusCode.UnAuthorized, Message = $"接口鉴权返回数据格式错误,鉴权接口返回数据格式必须为字典,且必须包含IsPermission的key" };
                                        return;
                                    }

                                    var isPermission = Convert.ToBoolean(checkPermissionResult["isPermission"]);
                                    if (!isPermission)
                                    {
                                        var actionName = filterContext.Route.ServiceDescriptor.GroupName().IsNullOrEmpty() ? filterContext.Route.ServiceDescriptor.RoutePath : filterContext.Route.ServiceDescriptor.GroupName();
                                        filterContext.Result = new HttpResultMessage<object> { IsSucceed = false, StatusCode = StatusCode.UnAuthorized, Message = $"没有请求{actionName}的权限" };
                                        return;
                                    }
                                    foreach (var kv in checkPermissionResult)
                                    {
                                        if (kv.Key == "isPermission")
                                        {
                                            continue;
                                        }

                                        if (kv.Value == null)
                                        {
                                            continue;
                                        }

                                        claimsIdentity.AddClaim(new Claim(kv.Key,kv.Value.ToString()));
                                    }
                                
                            }
                            
                            filterContext.Context.User = new ClaimsPrincipal(claimsIdentity);
                        }
                        else
                        {
                            if (filterContext.Route.ServiceDescriptor.EnableAuthorization())
                            {
                                filterContext.Result = new HttpResultMessage<object> { IsSucceed = false, StatusCode = CPlatform.Exceptions.StatusCode.UnAuthentication, Message = $"请先登录系统" };
                                return;
                            }
                            else
                            {
                                filterContext.Context.User = null;
                            }
                        }

                    }
                    else
                    {
                        if (filterContext.Route.ServiceDescriptor.EnableAuthorization())
                        {
                            filterContext.Result = new HttpResultMessage<object> { IsSucceed = false, StatusCode = CPlatform.Exceptions.StatusCode.UnAuthentication, Message = $"暂不支持{filterContext.Route.ServiceDescriptor.AuthType()}类型的身份认证方式" };
                        }
                    }

                }
            }

            if (string.Compare(filterContext.Path.ToLower(), gatewayAppConfig.TokenEndpointPath, true) == 0)
            {
                filterContext.Context.Items.Add("path", gatewayAppConfig.AuthenticationRoutePath);
            }
        }
    }
}

