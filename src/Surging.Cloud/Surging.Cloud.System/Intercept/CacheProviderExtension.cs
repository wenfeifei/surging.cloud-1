﻿using Surging.Cloud.CPlatform.Cache;
using Surging.Cloud.CPlatform.Exceptions;
using Surging.Cloud.CPlatform.Serialization;
using Surging.Cloud.CPlatform.Utilities;
using System;
using System.Threading.Tasks;

namespace Surging.Cloud.System.Intercept
{
    public static class CacheProviderExtension
    {
        public static async Task<T> GetFromCacheFirst<T>(this ICacheProvider cacheProvider, string key, Func<Task<T>> getFromPersistence, Type returnType, long? storeTime = null) where T : class
        {
            var serializer = ServiceLocator.GetService<ISerializer<string>>();
            T returnValue = default(T);
            try
            {
                var resultJson = cacheProvider.Get<string>(key);
                if (string.IsNullOrEmpty(resultJson) || resultJson == "\"[]\"")
                {
                    returnValue = await getFromPersistence();
                    if (returnValue != null && returnValue != "null")
                    {
                        resultJson = serializer.Serialize(returnValue);
                        if (storeTime.HasValue)
                        {
                            cacheProvider.Remove(key);
                            cacheProvider.Add(key, resultJson, storeTime.Value);
                        }
                        else
                        {
                            cacheProvider.Remove(key);
                            cacheProvider.Add(key, resultJson);
                        }
                    }
                }
                else
                {
                    returnValue = (T)serializer.Deserialize(resultJson, returnType);
                }
                return returnValue;
            }
            catch(Exception ex)
            {
                if (ex is BusinessException || ex.InnerException is BusinessException)
                {
                    throw;
                }
                returnValue = await getFromPersistence();
                return returnValue;
            }
        }

        public static async Task<T> GetFromCacheFirst<T>(this ICacheProvider cacheProvider, ICacheProvider l2cacheProvider, string l2Key, string key, Func<Task<T>> getFromPersistence, Type returnType, long? storeTime = null) where T : class
        {
            var serializer = ServiceLocator.GetService<ISerializer<string>>();
            object returnValue = default(T);
            try
            {
                var signJson = cacheProvider.Get<string>(key);
                if (string.IsNullOrEmpty(signJson) || signJson == "\"[]\"")
                {
                    returnValue = await getFromPersistence();
                    if (returnValue != null)
                    {
                        var resultJson = serializer.Serialize(returnValue); //JsonConvert.SerializeObject(returnValue);
                        var sign = Guid.NewGuid();
                        signJson = serializer.Serialize(sign); // JsonConvert.SerializeObject(sign);
                        if (l2Key == key)
                        {
                            SetCache(cacheProvider, key, signJson, storeTime);
                        }
                        SetCache(l2cacheProvider, l2Key, new ValueTuple<string, string>(signJson, resultJson), storeTime);
                    }
                }
                else
                {
                    var l2Cache = l2cacheProvider.Get<ValueTuple<string, string>>(l2Key);
                    if (l2Cache == default || l2Cache.Item1 != signJson)
                    {
                        returnValue = await getFromPersistence();
                        if (returnValue != null)
                        {
                            var resultJson = serializer.Serialize(returnValue); //JsonConvert.SerializeObject(returnValue);
                            SetCache(l2cacheProvider, l2Key, new ValueTuple<string, string>(signJson, resultJson), storeTime);
                        }
                    }
                    else
                    {
                        returnValue = serializer.Deserialize(l2Cache.Item2, returnType); //JsonConvert.DeserializeObject(l2Cache.Item2, returnType);
                    }
                }
                return returnValue as T;
            }
            catch (Exception ex)
            {
                if (ex is BusinessException || ex.InnerException is BusinessException)
                {
                    throw;
                }
                returnValue = await getFromPersistence();
                return returnValue as T;
            }
        }

        private static void SetCache(ICacheProvider cacheProvider, string key, object value, long? numOfMinutes)
        {
            if (numOfMinutes.HasValue)
            {
                cacheProvider.Remove(key);
                cacheProvider.Add(key, value, numOfMinutes.Value);
            }
            else
            {
                cacheProvider.Remove(key);
                cacheProvider.Add(key, value);
            }
        }
    }
}
