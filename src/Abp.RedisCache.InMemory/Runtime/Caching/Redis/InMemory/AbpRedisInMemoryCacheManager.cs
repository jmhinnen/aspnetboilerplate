﻿using System;
using System.Collections.Generic;
using System.Text;
using Abp.Dependency;
using Abp.Runtime.Caching.Configuration;
using Castle.Core.Logging;
using StackExchange.Redis;

namespace Abp.Runtime.Caching.Redis.InMemory
{
    public class AbpRedisInMemoryCacheManager : CacheManagerBase
    {
        public ILogger Logger { get; set; }

        private readonly AbpRedisCacheOptions _options;
        private readonly Lazy<ConnectionMultiplexer> _connectionMultiplexer;
        private IIocManager IocManager { get; set; }
    
        public AbpRedisInMemoryCacheManager(IIocManager iocManager, 
                    ICachingConfiguration configuration) : base(iocManager, configuration)
        {
            Logger = NullLogger.Instance;
            _options = iocManager.Resolve<AbpRedisCacheOptions>();
            _connectionMultiplexer = new Lazy<ConnectionMultiplexer>(CreateConnectionMultiplexer);
            IocManager = iocManager;

            IocManager.RegisterIfNot<AbpRedisCache>(DependencyLifeStyle.Transient);

            this.SetupEvents();
        }

        private ConnectionMultiplexer CreateConnectionMultiplexer()
        {
            return ConnectionMultiplexer.Connect(_options.ConnectionString);
        }

        private void SetupEvents()
        {

            ISubscriber subscriber = _connectionMultiplexer.Value.GetSubscriber();

            subscriber.Subscribe($"__keyspace*__:*", (channel, value) =>
                {
                    if (this.GetCacheNameAndKey(channel.ToString(), out string cacheName, out string key))
                    {

                        this.Caches.TryGetValue(cacheName, out ICache cache);

                        if (cache != null)
                        {
                            var castedCache = (AbpRedisInMemoryCache)cache;

                            if (value.ToString() == "set")
                            {
                                //events i'm interested in
                                //update - I can clear memory and refresh from cache

                                //HSET - Sets field in the hash stored at key to value. If key does not exist, a new key holding a hash is created.
                                //If field already exists in the hash, it is overwritten.

                                //DEL - Removes the specified keys. A key is ignored if it does not exist.

                                //EXPIRE - Set a timeout on key. After the timeout has expired, the key will automatically be deleted.
                                castedCache.SetMemoryOnly(key);
                            }
                            
                        }
                    }
                }
            );
        }

        private bool GetCacheNameAndKey(string redisLocalizedKeyName, out string cacheName, out string key)
        {
            //example value - __keyspace@0__:n:AbpUserSettingsCache,c:1
            cacheName = string.Empty;
            key = string.Empty;
            bool cacheFound = false;

            //split on the comma
            var commaSplit = redisLocalizedKeyName.Split(',');

            if (commaSplit.Length == 2)
            {
                var colonSplitCacheName = commaSplit[0].Split(':');
                var colonSplitKeyName = commaSplit[1].Split(':');

                if (colonSplitCacheName.Length == 3 && colonSplitKeyName.Length == 2)
                {
                    cacheName = colonSplitCacheName[2];
                    key = colonSplitKeyName[1];
                    cacheFound = true;
                }
            }

            return cacheFound;
        }

        protected override ICache CreateCacheImplementation(string name)
        {
            return new AbpRedisInMemoryCache(IocManager, name)
            {
                Logger = Logger
            };
        }

        protected override void DisposeCaches()
        {
            foreach (var cache in Caches.Values)
            {
                cache.Dispose();
            }
        }
    }
}
