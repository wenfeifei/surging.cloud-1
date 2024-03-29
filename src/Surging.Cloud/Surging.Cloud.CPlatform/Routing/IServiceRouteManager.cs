﻿using Surging.Cloud.CPlatform.Address;
using Surging.Cloud.CPlatform.Routing.Implementation;
using Surging.Cloud.CPlatform.Runtime.Server;
using Surging.Cloud.CPlatform.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Surging.Cloud.CPlatform.Routing
{
    /// <summary>
    /// 一个抽象的服务路由发现者。
    /// </summary>
    public interface IServiceRouteManager
    {

        /// <summary>
        /// 服务路由被创建。
        /// </summary>
        event EventHandler<ServiceRouteEventArgs> Created;

        /// <summary>
        /// 服务路由被删除。
        /// </summary>
        event EventHandler<ServiceRouteEventArgs> Removed;

        /// <summary>
        /// 服务路由被修改。
        /// </summary>
        event EventHandler<ServiceRouteChangedEventArgs> Changed;

        /// <summary>
        /// 获取所有可用的服务路由信息。
        /// </summary>
        /// <returns>服务路由集合。</returns>
        Task<IEnumerable<ServiceRoute>> GetRoutesAsync(bool needUpdateFromServiceCenter = false);

        Task<ServiceRoute> GetRouteByPathAsync(string path, string httpMethod);

        Task<ServiceRoute> GetRouteByServiceIdAsync(string serviceId, bool isCache = true);

        /// <summary>
        /// 设置服务路由。
        /// </summary>
        /// <param name="routes">服务路由集合。</param>
        /// <returns>一个任务。</returns>
        Task SetRoutesAsync(IEnumerable<ServiceRoute> routes);

        Task SetRouteAsync(ServiceRoute route);

        /// <summary>
        /// 移除地址列表
        /// </summary>
        /// <param name="routes">地址列表。</param>
        /// <returns>一个任务。</returns>
        Task RemveAddressAsync(IEnumerable<AddressModel> Address);

        /// <summary>
        /// 移除指定服务的地址列表
        /// </summary>
        /// <param name="Address"></param>
        /// <param name="serviceId"></param>
        /// <returns></returns>
        Task RemveAddressAsync(IEnumerable<AddressModel> Address, string serviceId);

        /// <summary>
        /// 清空所有的服务路由。
        /// </summary>
        /// <returns>一个任务。</returns>
        Task ClearAsync();
        
    }

    /// <summary>
    /// 服务路由管理者扩展方法。
    /// </summary>
    public static class ServiceRouteManagerExtensions
    {
        /// <summary>
        /// 根据服务Id获取一个服务路由。
        /// </summary>
        /// <param name="serviceRouteManager">服务路由管理者。</param>
        /// <param name="serviceId">服务Id。</param>
        /// <returns>服务路由。</returns>
        public static async Task<ServiceRoute> GetAsync(this IServiceRouteManager serviceRouteManager, string serviceId)
        {
            return await serviceRouteManager.GetRouteByServiceIdAsync(serviceId);
        }

        public static async Task<ICollection<ServiceRoute>> GetLocalServiceRoutes(this IServiceRouteManager serviceRouteManager, IEnumerable<ServiceEntry> serviceEntries)
        {
            var serviceRoutes = await serviceRouteManager.GetRoutesAsync();
            var localServiceRoutes = new List<ServiceRoute>();
            foreach (var entry in serviceEntries) 
            {
                var serviceRoute = serviceRoutes.FirstOrDefault(p => p.ServiceDescriptor.Id == entry.Descriptor.Id);
                if (serviceRoute != null) 
                {
                    localServiceRoutes.Add(serviceRoute);
                }
            }
            return localServiceRoutes;
        }

        /// <summary>
        /// 获取地址
        /// </summary>
        /// <returns></returns>
        public static async Task<IEnumerable<AddressModel>> GetAddressAsync(this IServiceRouteManager serviceRouteManager, string condition = null)
        {
            var routes = await serviceRouteManager.GetRoutesAsync();
            Dictionary<string, AddressModel> result = new Dictionary<string, AddressModel>();
            if (condition != null)
            {
                if (!condition.IsIP())
                {
                    routes = routes.Where(p => p.ServiceDescriptor.Id == condition);
                }
                else
                {
                    routes = routes.Where(p => p.Address.Any(m => m.ToString() == condition));
                    var addresses = routes.FirstOrDefault().Address;
                    return addresses.Where(p => p.ToString() == condition);
                }
            }

            foreach (var route in routes)
            {
                var addresses = route.Address;
                foreach (var address in addresses)
                {
                    if (!result.ContainsKey(address.ToString()))
                    {
                        result.Add(address.ToString(), address);
                    }
                }
            }
            return result.Values;
        }

        public static async Task<IEnumerable<ServiceRoute>> GetRoutesAsync(this IServiceRouteManager serviceRouteManager, string address)
        {
            var routes = await serviceRouteManager.GetRoutesAsync();
            return routes.Where(p => p.Address.Any(m => m.ToString() == address));
        }

        public static async Task<IEnumerable<ServiceDescriptor>> GetServiceDescriptorAsync(this IServiceRouteManager serviceRouteManager, string address, string serviceId = null)
        {
            var routes = await serviceRouteManager.GetRoutesAsync();
            if (serviceId == null)
            {
                return routes.Where(p => p.Address.Any(m => m.ToString() == address))
                 .Select(p => p.ServiceDescriptor);
            }
            else
            {
                return routes.Where(p => p.ServiceDescriptor.Id == serviceId)
               .Select(p => p.ServiceDescriptor);
            }
        }
    }
}