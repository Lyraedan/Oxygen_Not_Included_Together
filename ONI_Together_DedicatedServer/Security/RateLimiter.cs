using System;
using System.Collections.Generic;

namespace ONI_Together_DedicatedServer.Security
{
    /// <summary>
    /// Simple per-key token-bucket rate limiter. Thread-safe.
    /// </summary>
    public sealed class RateLimiter
    {
        private readonly int _capacity;
        private readonly double _refillPerSecond;
        private readonly Dictionary<string, Bucket> _buckets = new();
        private readonly object _gate = new();

        public RateLimiter(int capacity, double refillPerSecond)
        {
            _capacity = Math.Max(1, capacity);
            _refillPerSecond = Math.Max(0.0, refillPerSecond);
        }

        public bool TryAcquire(string key, int tokens = 1)
        {
            if (string.IsNullOrEmpty(key))
                key = "_unknown";
            if (tokens <= 0)
                return true;

            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if (!_buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new Bucket(_capacity, now);
                    _buckets[key] = bucket;
                }

                // Refill.
                double elapsed = (now - bucket.LastRefillUtc).TotalSeconds;
                if (elapsed > 0 && _refillPerSecond > 0)
                {
                    bucket.Tokens = Math.Min(_capacity, bucket.Tokens + elapsed * _refillPerSecond);
                    bucket.LastRefillUtc = now;
                }

                if (bucket.Tokens >= tokens)
                {
                    bucket.Tokens -= tokens;
                    return true;
                }
                return false;
            }
        }

        public void Reset(string key)
        {
            lock (_gate) { _buckets.Remove(key); }
        }

        private sealed class Bucket
        {
            public double Tokens;
            public DateTime LastRefillUtc;
            public Bucket(double tokens, DateTime lastRefillUtc) { Tokens = tokens; LastRefillUtc = lastRefillUtc; }
        }
    }
}
