﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Surging.Cloud.CPlatform.Cache;
using Surging.Cloud.CPlatform.Module;
using Surging.Cloud.CPlatform.Mqtt;
using Surging.Cloud.CPlatform.Routing;
using Surging.Cloud.CPlatform.Runtime.Client;
using Surging.Cloud.CPlatform.Runtime.Server;
using Surging.Cloud.CPlatform.Serialization;
using Surging.Cloud.CPlatform.Support;
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
    public class ZookeeperModule : EnginePartModule
    {
        public override void Initialize(AppModuleContext context)
        {
            base.Initialize(context);
        }

        /// <summary>
        /// Inject dependent third-party components
        /// </summary>
        /// <param name="builder"></param>
        protected override void RegisterBuilder(ContainerBuilderWrapper builder)
        {
            base.RegisterBuilder(builder);
            var configInfo = new ConfigInfo(null);
            UseZookeeperAddressSelector(builder)
            .UseHealthCheck(builder)
            .UseZookeeperClientProvider(builder, configInfo)
            .UseZooKeeperRouteManager(builder, configInfo)
            .UseZooKeeperCacheManager(builder, configInfo)
            .UseZooKeeperMqttRouteManager(builder, configInfo)
            .UseZooKeeperServiceSubscribeManager(builder, configInfo)
            .UseZooKeeperCommandManager(builder, configInfo);
        }

        public ContainerBuilderWrapper UseRouteManager(ContainerBuilderWrapper builder, Func<IServiceProvider, IServiceRouteManager> factory)
        {
            builder.RegisterAdapter(factory).InstancePerLifetimeScope();
            return builder;
        }

        public ContainerBuilderWrapper UseSubscribeManager(ContainerBuilderWrapper builder, Func<IServiceProvider, IServiceSubscribeManager> factory)
        {
            builder.RegisterAdapter(factory).InstancePerLifetimeScope();
            return builder;
        }

        public ContainerBuilderWrapper UseCommandManager(ContainerBuilderWrapper builder, Func<IServiceProvider, IServiceCommandManager> factory)
        {
            builder.RegisterAdapter(factory).InstancePerLifetimeScope();
            return builder;
        }

        public ContainerBuilderWrapper UseCacheManager(ContainerBuilderWrapper builder, Func<IServiceProvider, IServiceCacheManager> factory)
        {
            builder.RegisterAdapter(factory).InstancePerLifetimeScope();
            return builder;
        }

        public ContainerBuilderWrapper UseMqttRouteManager(ContainerBuilderWrapper builder, Func<IServiceProvider, IMqttServiceRouteManager> factory)
        {
            builder.RegisterAdapter(factory).InstancePerLifetimeScope();
            return builder;
        }

        public ContainerBuilderWrapper UseZooKeeperClientProvider(ContainerBuilderWrapper builder, Func<IServiceProvider, IZookeeperClientProvider> factory)
        {
            builder.RegisterAdapter(factory).InstancePerLifetimeScope();
            return builder;
        }

        /// <summary>
        /// 设置共享文件路由管理者。
        /// </summary>
        /// <param name="builder">Rpc服务构建者。</param>
        /// <param name="configInfo">ZooKeeper设置信息。</param>
        /// <returns>服务构建者。</returns>
        public ZookeeperModule UseZooKeeperRouteManager(ContainerBuilderWrapper builder, ConfigInfo configInfo)
        {
            UseRouteManager(builder, provider =>
            new ZooKeeperServiceRouteManager(
             GetConfigInfo(configInfo),
             provider.GetRequiredService<ISerializer<byte[]>>(),
             provider.GetRequiredService<ISerializer<string>>(),
             provider.GetRequiredService<IServiceRouteFactory>(),
             provider.GetRequiredService<ILogger<ZooKeeperServiceRouteManager>>(),
             provider.GetRequiredService<IZookeeperClientProvider>(),
             provider.GetRequiredService<ILockerProvider>()));
            return this;
        }

        public ZookeeperModule UseZooKeeperMqttRouteManager(ContainerBuilderWrapper builder, ConfigInfo configInfo)
        {
            UseMqttRouteManager(builder, provider =>
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
            return this;
        }


        /// <summary>
        /// 设置服务命令管理者。
        /// </summary>
        /// <param name="builder">Rpc服务构建者。</param>
        /// <param name="configInfo">ZooKeeper设置信息。</param>
        /// <returns>服务构建者。</returns>
        public ZookeeperModule UseZooKeeperCommandManager(ContainerBuilderWrapper builder, ConfigInfo configInfo)
        {
            UseCommandManager(builder, provider =>
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
            return this;
        }

        public ZookeeperModule UseZooKeeperServiceSubscribeManager(ContainerBuilderWrapper builder, ConfigInfo configInfo)
        {
             UseSubscribeManager(builder,provider =>
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
            return this;
        }

        public ZookeeperModule UseZooKeeperCacheManager(ContainerBuilderWrapper builder, ConfigInfo configInfo)
        {
            UseCacheManager(builder, provider =>
             new ZookeeperServiceCacheManager(
               GetConfigInfo(configInfo),
              provider.GetRequiredService<ISerializer<byte[]>>(),
                provider.GetRequiredService<ISerializer<string>>(),
                provider.GetRequiredService<IServiceCacheFactory>(),
                provider.GetRequiredService<ILogger<ZookeeperServiceCacheManager>>(),
                  provider.GetRequiredService<IZookeeperClientProvider>()));
            return this;
        }

        public ZookeeperModule UseZookeeperClientProvider(ContainerBuilderWrapper builder, ConfigInfo configInfo)
        {
            UseZooKeeperClientProvider(builder, provider =>
        new DefaultZookeeperClientProvider(
            GetConfigInfo(configInfo),
         provider.GetRequiredService<IHealthCheckService>(),
           provider.GetRequiredService<IZookeeperAddressSelector>(),
           provider.GetRequiredService<ILogger<DefaultZookeeperClientProvider>>()));
            return this;
        }

        public ZookeeperModule UseZookeeperAddressSelector(ContainerBuilderWrapper builder)
        {
            builder.RegisterType<ZookeeperRandomAddressSelector>().As<IZookeeperAddressSelector>().SingleInstance();
            return this;
        }

        public ZookeeperModule UseHealthCheck(ContainerBuilderWrapper builder)
        {
            builder.RegisterType<DefaultHealthCheckService>().As<IHealthCheckService>().SingleInstance();
            return this;
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
