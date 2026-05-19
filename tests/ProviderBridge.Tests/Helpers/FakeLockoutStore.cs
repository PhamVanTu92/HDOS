extern alias RequestApi;

using RequestApi::ReportingPlatform.RequestApi.Services;

namespace ReportingPlatform.ProviderBridge.Tests.Helpers;

/// <summary>
/// In-memory fake for ProviderLockoutService's Redis operations.
/// Uses real ProviderLockoutService backed by NSubstitute IDatabase.
/// </summary>
public sealed class FakeLockoutStore
{
    private readonly Dictionary<string, (long value, DateTimeOffset? expiry)> _store = new();

    public long Increment(string key)
    {
        if (!_store.TryGetValue(key, out var entry) || IsExpired(entry.expiry))
            entry = (0, null);
        var newVal = entry.value + 1;
        _store[key] = (newVal, entry.expiry);
        return newVal;
    }

    public void SetExpiry(string key, TimeSpan ttl)
    {
        if (_store.TryGetValue(key, out var entry))
            _store[key] = (entry.value, DateTimeOffset.UtcNow.Add(ttl));
    }

    public bool Exists(string key) =>
        _store.TryGetValue(key, out var entry) && !IsExpired(entry.expiry);

    public void Set(string key, string value, TimeSpan ttl) =>
        _store[key] = (1, DateTimeOffset.UtcNow.Add(ttl));

    public void Delete(string key) => _store.Remove(key);

    public void ExpireAll() // for TTL-expiry simulation
    {
        foreach (var k in _store.Keys.ToList())
            _store[k] = (_store[k].value, DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    private static bool IsExpired(DateTimeOffset? expiry) =>
        expiry.HasValue && expiry.Value < DateTimeOffset.UtcNow;

    /// <summary>Builds a ProviderLockoutService backed by this fake store.</summary>
    public ProviderLockoutService BuildLockoutService()
    {
        var db = Substitute.For<IDatabase>();

        // StringIncrementAsync
        db.StringIncrementAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
          .Returns(ci => Task.FromResult(Increment(((RedisKey)ci[0]!).ToString()!)));

        // KeyExpireAsync (the overload with ExpireWhen)
        db.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>())
          .Returns(ci =>
          {
              var key = ((RedisKey)ci[0]!).ToString()!;
              var ttl = (TimeSpan?)ci[1];
              if (ttl.HasValue) SetExpiry(key, ttl.Value);
              return Task.FromResult(true);
          });

        // KeyExpireAsync (the overload WITHOUT ExpireWhen — used by ProviderLockoutService)
        db.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<CommandFlags>())
          .Returns(ci =>
          {
              var key = ((RedisKey)ci[0]!).ToString()!;
              var ttl = (TimeSpan?)ci[1];
              if (ttl.HasValue) SetExpiry(key, ttl.Value);
              return Task.FromResult(true);
          });

        // KeyExistsAsync
        db.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
          .Returns(ci => Task.FromResult(Exists(((RedisKey)ci[0]!).ToString()!)));

        // StringSetAsync (for lockout key)
        db.StringSetAsync(
              Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
              Arg.Any<TimeSpan?>(), Arg.Any<bool>(),
              Arg.Any<When>(), Arg.Any<CommandFlags>())
          .Returns(ci =>
          {
              var key = ((RedisKey)ci[0]!).ToString()!;
              var ttl = (TimeSpan?)ci[2];
              Set(key, "1", ttl ?? TimeSpan.FromMinutes(5));
              return Task.FromResult(true);
          });

        // KeyDeleteAsync
        db.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
          .Returns(ci =>
          {
              Delete(((RedisKey)ci[0]!).ToString()!);
              return Task.FromResult(true);
          });

        return new ProviderLockoutService(db);
    }
}
