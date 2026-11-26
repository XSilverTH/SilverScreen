namespace SilverScreen.Infrastructure.Common;

/// <summary>
///     Thread-safe bounded LRU cache with optional per-entry TTL expiration.
/// </summary>
internal sealed class BoundedLruCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _lookup;
    private readonly LinkedList<CacheItem> _lru = new();
    private readonly Lock _gate = new();
    private readonly int _capacity;
    private readonly TimeProvider _timeProvider;

    public BoundedLruCache(
        int capacity,
        IEqualityComparer<TKey>? comparer = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        _capacity = capacity;
        _lookup = new Dictionary<TKey, LinkedListNode<CacheItem>>(comparer);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (!_lookup.TryGetValue(key, out var node))
            {
                value = default!;
                return false;
            }

            if (node.Value.ExpiresAt is { } expiresAt && now >= expiresAt)
            {
                _lru.Remove(node);
                _lookup.Remove(key);
                value = default!;
                return false;
            }

            if (node != _lru.Last)
            {
                _lru.Remove(node);
                _lru.AddLast(node);
            }

            value = node.Value.Value;
            return true;
        }
    }

    public void Set(TKey key, TValue value, TimeSpan? ttl = null)
    {
        DateTimeOffset? expiresAt = ttl is { } duration
            ? _timeProvider.GetUtcNow() + duration
            : null;

        lock (_gate)
        {
            if (_lookup.TryGetValue(key, out var existing))
            {
                existing.Value = new CacheItem(key, value, expiresAt);
                if (existing != _lru.Last)
                {
                    _lru.Remove(existing);
                    _lru.AddLast(existing);
                }

                return;
            }

            var node = _lru.AddLast(new CacheItem(key, value, expiresAt));
            _lookup[key] = node;

            while (_lookup.Count > _capacity && _lru.First is { } oldest)
            {
                _lru.RemoveFirst();
                _lookup.Remove(oldest.Value.Key);
            }
        }
    }

    public bool Remove(TKey key)
    {
        lock (_gate)
        {
            if (!_lookup.TryGetValue(key, out var node))
                return false;

            _lru.Remove(node);
            _lookup.Remove(key);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lookup.Clear();
            _lru.Clear();
        }
    }

    private readonly record struct CacheItem(TKey Key, TValue Value, DateTimeOffset? ExpiresAt);
}
