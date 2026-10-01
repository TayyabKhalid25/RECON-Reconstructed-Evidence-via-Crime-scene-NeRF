using System.Collections.Generic;
using UnityEngine;

namespace Gsplat
{
    public static class GsplatResourceManager
    {
        class Cache
        {
            public GsplatResource Resource;
            public int RefCount;
        }

        static readonly Dictionary<GsplatAsset, Cache> k_resourceCache = new();

        public static GsplatResource Get(GsplatAsset asset)
        {
            if (!asset)
                throw new System.ArgumentNullException(nameof(asset));

            if (k_resourceCache.TryGetValue(asset, out var cache))
            {
                cache.RefCount++;
                return cache.Resource;
            }

            cache = new Cache
            {
                Resource = asset.CreateResource(),
                RefCount = 1
            };
            k_resourceCache[asset] = cache;
            return cache.Resource;
        }

        public static void Release(GsplatAsset asset)
        {
            // A destroyed Unity object compares equal to null, but its managed reference is
            // still the cache key and must be released.
            if (ReferenceEquals(asset, null))
                return;
            if (!k_resourceCache.TryGetValue(asset, out var cache))
            {
                Debug.LogWarning("Trying to release a GPU resource that is not cached.");
                return;
            }

            cache.RefCount--;
            if (cache.RefCount != 0) return;
            cache.Resource.Dispose();
            k_resourceCache.Remove(asset);
        }
    }
}
