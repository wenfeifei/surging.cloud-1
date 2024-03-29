﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Surging.Cloud.CPlatform.Cache;
using Surging.Cloud.CPlatform.Utilities;

namespace Surging.Cloud.Caching.HealthChecks.Implementation
{
    /// <summary>
    /// 默认的健康检查服务
    /// </summary>
    public class DefaultHealthCheckService : IHealthCheckService, IDisposable
    {
        private readonly Timer _timer;
        private readonly ConcurrentDictionary<Tuple<string, int>, MonitorEntry> _dictionary =
    new ConcurrentDictionary<Tuple<string, int>, MonitorEntry>();
        private readonly IServiceCacheManager _serviceCacheManager;
        private readonly ILogger<DefaultHealthCheckService> _logger;

        public DefaultHealthCheckService(IServiceCacheManager serviceCacheManager)
        {
            _logger = ServiceLocator.GetService<ILogger<DefaultHealthCheckService>>();
            var timeSpan = TimeSpan.FromSeconds(10);
            _serviceCacheManager = serviceCacheManager;
            _timer = new Timer(async s =>
            {
                await Check(_dictionary.ToArray().Select(i => i.Value));
                //RemoveUnhealthyAddress(_dictionary.ToArray().Select(i => i.Value).Where(m => m.UnhealthyTimes >= 6));
            }, null, timeSpan, timeSpan);

            //去除监控。
            _serviceCacheManager.Removed += (s, e) =>
            {
                Remove(e.Cache.CacheEndpoint);
            };

            //重新监控。
            _serviceCacheManager.Created += async (s, e) =>
            {
                var keys = e.Cache.CacheEndpoint.Select(cacheEndpoint =>
                {
                    return new Tuple<string, int>(cacheEndpoint.Host, cacheEndpoint.Port);
                });
                await Check(_dictionary.Where(i => keys.Contains(i.Key)).Select(i => i.Value));
            };
            //重新监控。
            _serviceCacheManager.Changed += async (s, e) =>
            {
                var keys = e.Cache.CacheEndpoint.Select(cacheEndpoint =>
                {
                    return new Tuple<string, int>(cacheEndpoint.Host, cacheEndpoint.Port);
                });
                await Check(_dictionary.Where(i => keys.Contains(i.Key)).Select(i => i.Value));
            };
        }

        public async Task<bool> IsHealth(CacheEndpoint address, string cacheId)
        {
            MonitorEntry entry;
            return !_dictionary.TryGetValue(new Tuple<string, int>(address.Host, address.Port), out entry) ?  await Check(address, cacheId) : entry.Health;
        }

        public Task MarkFailure(CacheEndpoint address, string cacheId)
        {
            return Task.Run(() =>
            {
                var entry = _dictionary.GetOrAdd(new Tuple<string, int>(address.Host, address.Port), k => new MonitorEntry(address, cacheId, false));
                entry.Health = false;
            });
        }

        public void Monitor(CacheEndpoint address, string cacheId)
        {
            _dictionary.GetOrAdd(new Tuple<string, int>(address.Host, address.Port), k => new MonitorEntry(address, cacheId));
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        private async Task<bool> Check(CacheEndpoint address, string id)
        {
            return await CacheContainer.GetService<ICacheProvider>(id)
            .ConnectionAsync(address);
        }

        private async Task Check(IEnumerable<MonitorEntry> entrys)
        {
            foreach (var entry in entrys)
            {
                try
                {
                    await CacheContainer.GetService<ICacheProvider>(entry.CacheId).ConnectionAsync(entry.EndPoint);
                    entry.UnhealthyTimes = 0;
                    entry.Health = true;
                }
                catch
                {
                    if (entry.UnhealthyTimes >= 6)
                    {
                        _logger.LogWarning($"服务地址{entry.EndPoint}不健康,UnhealthyTimes={entry.UnhealthyTimes},服务将会被移除");
                        await RemoveUnhealthyAddress(entry);
                    }
                    else
                    {
                        entry.UnhealthyTimes++;
                        entry.Health = false;
                        _logger.LogWarning($"服务地址{entry.EndPoint}不健康,UnhealthyTimes={entry.UnhealthyTimes}");
                    }
                }
            }
        }

        private void Remove(IEnumerable<CacheEndpoint> cacheEndpoints)
        {
            foreach (var cacheEndpoint in cacheEndpoints)
            {
                MonitorEntry value;
                _dictionary.TryRemove(new Tuple<string, int>(cacheEndpoint.Host, cacheEndpoint.Port), out value);
            }
        }


        private void RemoveUnhealthyAddress(IEnumerable<MonitorEntry> monitorEntry)
        {
            if (monitorEntry.Any())
            {
                var addresses = monitorEntry.Select(p => p.EndPoint).ToList();
                _serviceCacheManager.RemveAddressAsync(addresses).Wait();
                addresses.ForEach(p => {

                    _dictionary.TryRemove(new Tuple<string, int>(p.Host, p.Port), out MonitorEntry value);
                });

            }
        }
        private async Task RemoveUnhealthyAddress(MonitorEntry monitorEntry)
        {
            var addresses = monitorEntry.EndPoint;
            await _serviceCacheManager.RemveAddressAsync(new List<CacheEndpoint>() { addresses } );
            _dictionary.TryRemove(new Tuple<string, int>(addresses.Host, addresses.Port), out MonitorEntry value);
        }

        #region Help Class

        protected class MonitorEntry
        {
            public MonitorEntry(CacheEndpoint address, string cacheId, bool health = true)
            {
                EndPoint = address;
                Health = health;
                CacheId = cacheId;
            }

            public int UnhealthyTimes { get; set; }

            public string CacheId { get; set; }
            public CacheEndpoint EndPoint { get; set; }
            public bool Health { get; set; }
        }

        #endregion Help Class
    }
}