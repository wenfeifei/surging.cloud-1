﻿using Autofac;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Surging.Cloud.CPlatform;
using Surging.Cloud.CPlatform.Cache;
using Surging.Cloud.CPlatform.Mqtt;
using Surging.Cloud.CPlatform.Routing;
using Surging.Cloud.CPlatform.Runtime.Client;
using Surging.Cloud.CPlatform.Runtime.Server;
using Surging.Cloud.CPlatform.Serialization;
using Surging.Cloud.Lock.Provider;
using Surging.Cloud.Zookeeper.Configurations;
using Surging.Cloud.Zookeeper.Internal;
using Surging.Cloud.Zookeeper.Internal.Cluster.HealthChecks;
using Surging.Cloud.Zookeeper.Internal.Cluster.HealthChecks.Implementation;
using Surging.Cloud.Zookeeper.Internal.Cluster.Implementation.Selectors;
using Surging.Cloud.Zookeeper.Internal.Cluster.Implementation.Selectors.Implementation;
using Surging.Cloud.Zookeeper.Internal.Implementation;
using System;

namespace Surging.Cloud.Zookeeper
{
    public static class ContainerBuilderExtensions
    {
        /// <summary>
        /// 设置共享文件路由管理者。
        /// </summary>
        /// <param name="builder">Rpc服务构建者。</param>
        /// <param name="configInfo">ZooKeeper设置信息。</param>
        /// <returns>服务构建者。</returns>
        public static IServiceBuilder UseZooKeeperRouteManager(this IServiceBuilder builder, ConfigInfo configInfo)
        {
            return builder.UseRouteManager(provider =>
             new ZooKeeperServiceRouteManager(
                GetConfigInfo(configInfo),
              provider.GetRequiredService<ISerializer<byte[]>>(),
                provider.GetRequiredService<ISerializer<string>>(),
                provider.GetRequiredService<IServiceRouteFactory>(),
                provider.GetRequiredService<ILogger<ZooKeeperServiceRouteManager>>(),
                provider.GetRequiredService<IZookeeperClientProvider>(),
                provider.GetRequiredService<ILockerProvider>()
                  ));
        }

        public static IServiceBuilder UseZooKeeperMqttRouteManager(this IServiceBuilder builder, ConfigInfo configInfo)
        {
            return builder.UseMqttRouteManager(provider =>
            {
                var result = new ZooKeeperMqttServiceRouteManager(
                     GetConfigInfo(configInfo),
                    provider.GetRequiredService<ISerializer<byte[]>>(),
                     provider.GetRequiredService<ISerializer<string>>(),
                     provider.GetRequiredService<IMqttServiceFactory>(),
                     provider.GetRequiredService<ILogger<ZooKeeperMqttServiceRouteManager>>(),
                    provider.GetRequiredService<IZookeeperClientProvider>(),
                    provider.GetRequiredService<ILockerProvider>());
                return result;
            });
        }

        /// <summary>
        /// 设置服务命令管理者。
        /// </summary>
        /// <param name="builder">Rpc服务构建者。</param>
        /// <param name="configInfo">ZooKeeper设置信息。</param>
        /// <returns>服务构建者。</returns>
        public static IServiceBuilder UseZooKeeperCommandManager(this IServiceBuilder builder, ConfigInfo configInfo)
        {
            return builder.UseCommandManager(provider =>
            {
                var result = new ZookeeperServiceCommandManager(
                    GetConfigInfo(configInfo),
                  provider.GetRequiredService<ISerializer<byte[]>>(),
                    provider.GetRequiredService<ISerializer<string>>(),
                  provider.GetRequiredService<IServiceRouteManager>(),
                    provider.GetRequiredService<IServiceEntryManager>(),
                    provider.GetRequiredService<ILogger<ZookeeperServiceCommandManager>>(),
                  provider.GetRequiredService<IZookeeperClientProvider>());
                return result;
            });
        }

        public static IServiceBuilder UseZooKeeperServiceSubscribeManager(this IServiceBuilder builder, ConfigInfo configInfo)
        {
            return builder.UseSubscribeManager(provider =>
            {
                var result = new ZooKeeperServiceSubscribeManager(
                    GetConfigInfo(configInfo),
                  provider.GetRequiredService<ISerializer<byte[]>>(),
                    provider.GetRequiredService<ISerializer<string>>(),
                    provider.GetRequiredService<IServiceSubscriberFactory>(),
                    provider.GetRequiredService<ILogger<ZooKeeperServiceSubscribeManager>>(),
                  provider.GetRequiredService<IZookeeperClientProvider>());
                return result;
            });
        }

        public static IServiceBuilder UseZooKeeperCacheManager(this IServiceBuilder builder, ConfigInfo configInfo)
        {
            return builder.UseCacheManager(provider =>
             new ZookeeperServiceCacheManager(
               GetConfigInfo(configInfo),
              provider.GetRequiredService<ISerializer<byte[]>>(),
                provider.GetRequiredService<ISerializer<string>>(),
                provider.GetRequiredService<IServiceCacheFactory>(),
                provider.GetRequiredService<ILogger<ZookeeperServiceCacheManager>>(),
                  provider.GetRequiredService<IZookeeperClientProvider>()));
        }


        public static IServiceBuilder UseZooKeeperManager(this IServiceBuilder builder, ConfigInfo configInfo)
        {
            return builder.UseZooKeeperRouteManager(configInfo)
                .UseHealthCheck()
                .UseZookeeperAddressSelector()
                .UseZookeeperClientProvider(configInfo)
                .UseZooKeeperCacheManager(configInfo)
                .UseZooKeeperServiceSubscribeManager(configInfo)
                .UseZooKeeperCommandManager(configInfo)
                .UseZooKeeperMqttRouteManager(configInfo);
        }

        public static IServiceBuilder UseZooKeeperManager(this IServiceBuilder builder)
        {
            var configInfo = new ConfigInfo(null);
            return builder.UseZooKeeperRouteManager(configInfo)
                .UseHealthCheck()
                .UseZookeeperAddressSelector()
                .UseZookeeperClientProvider(configInfo)
                .UseZooKeeperCacheManager(configInfo)
                .UseZooKeeperServiceSubscribeManager(configInfo)
                .UseZooKeeperCommandManager(configInfo)
                .UseZooKeeperMqttRouteManager(configInfo);
        }

        public static IServiceBuilder UseZookeeperAddressSelector(this IServiceBuilder builder)
        {
            builder.Services.RegisterType<ZookeeperRandomAddressSelector>().As<IZookeeperAddressSelector>().SingleInstance();
            return builder;
        }

        public static IServiceBuilder UseHealthCheck(this IServiceBuilder builder)
        {
            builder.Services.RegisterType<DefaultHealthCheckService>().As<IHealthCheckService>().SingleInstance();
            return builder;
        }


        public static IServiceBuilder UseZookeeperClientProvider(this IServiceBuilder builder, ConfigInfo configInfo)
        {
            builder.Services.Register(provider =>
       new DefaultZookeeperClientProvider(
           GetConfigInfo(configInfo),
        provider.Resolve<IHealthCheckService>(),
          provider.Resolve<IZookeeperAddressSelector>(),
          provider.Resolve<ILogger<DefaultZookeeperClientProvider>>())).As<IZookeeperClientProvider>().SingleInstance();
            return builder;
        }


        private static ConfigInfo GetConfigInfo(ConfigInfo config)
        {
            ZookeeperOption option = null;
            var section = CPlatform.AppConfig.GetSection("Zookeeper");
            if (section.Exists())
                option = section.Get<ZookeeperOption>();
            else if (AppConfig.Configuration != null)
                option = AppConfig.Configuration.Get<ZookeeperOption>();
            if (option != null)
            {
                var sessionTimeout = config.SessionTimeout.TotalSeconds;
                var connectionTimeout = config.ConnectionTimeout.TotalSeconds;
                var operatingTimeout = config.OperatingTimeout.TotalSeconds;
                if (option.SessionTimeout > 0) 
                {
                    sessionTimeout = option.SessionTimeout;
                }
                if (option.ConnectionTimeout > 0) 
                {
                    connectionTimeout = option.ConnectionTimeout;
                }
                if (option.OperatingTimeout > 0)
                {
                    operatingTimeout = option.OperatingTimeout;
                }

                config = new ConfigInfo(
                    option.ConnectionString,
                    TimeSpan.FromSeconds(sessionTimeout),
                    TimeSpan.FromSeconds(connectionTimeout),
                    TimeSpan.FromSeconds(operatingTimeout),
                    option.RoutePath ?? config.RoutePath,
                    option.SubscriberPath ?? config.SubscriberPath,
                    option.CommandPath ?? config.CommandPath,
                    option.CachePath ?? config.CachePath,
                    option.MqttRoutePath ?? config.MqttRoutePath,
                    option.ChRoot ?? config.ChRoot,
                    option.ReloadOnChange != null ? bool.Parse(option.ReloadOnChange) :
                    config.ReloadOnChange,
                   option.EnableChildrenMonitor != null ? bool.Parse(option.EnableChildrenMonitor) :
                    config.EnableChildrenMonitor
                   );
            }
            return config;
        }
    }
}
