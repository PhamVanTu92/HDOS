using System.Collections.Concurrent;
using NSubstitute;
using ReportingPlatform.Contracts.Responses;
using ReportingPlatform.Contracts.Validation;
using ReportingPlatform.Providers.Abstractions;
using ReportingPlatform.Providers.Models;
using StackExchange.Redis;
// Disambiguate: IMainHubClient uses HubContracts.ResponseDispatchPushMessage (MessagePack annotated).
// Contracts.Responses also has a type with the same simple name — alias to resolve ambiguity.
using ResponseDispatchPushMessage = ReportingPlatform.HubContracts.ResponseDispatchPushMessage;

namespace ReportingPlatform.Gateway.Tests.Helpers;

// ── FakeOperationRegistry ────────────────────────────────────────────────────

public sealed class FakeOperationRegistry : IOperationRegistry
{
    private readonly OperationRegistration? _registration;

    public FakeOperationRegistry(OperationRegistration? registration = null) =>
        _registration = registration;

    public Task<OperationRegistration?> ResolveAsync(string operation, CancellationToken ct = default) =>
        Task.FromResult(_registration);

    public Task<IReadOnlyList<OperationRegistration>> GetAllActiveAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OperationRegistration>>([]);

    public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;

    public static FakeOperationRegistry Active(string op = "test.op") =>
        new(new OperationRegistration
        {
            OperationPattern = op,
            HandlerType      = "internal",
            Status           = "active",
            TimeoutMs        = 30_000,
        });

    public static FakeOperationRegistry Unknown() => new(registration: null);
}

// ── FakeParamsValidator ──────────────────────────────────────────────────────

public sealed class FakeParamsValidator : IParamsValidator
{
    private readonly bool _isValid;
    private readonly IReadOnlyList<ValidationError>? _errors;

    private FakeParamsValidator(bool isValid, IReadOnlyList<ValidationError>? errors = null)
    {
        _isValid = isValid;
        _errors  = errors;
    }

    public Task<ValidationResult> ValidateAsync(
        string operationPattern, JsonElement @params, CancellationToken ct = default) =>
        Task.FromResult(_isValid
            ? ValidationResult.Success
            : ValidationResult.Failure(_errors ?? [
                new ValidationError { Field = "params", Message = "invalid", Code = "INVALID" },
            ]));

    public static FakeParamsValidator AlwaysValid()   => new(isValid: true);
    public static FakeParamsValidator AlwaysInvalid() => new(isValid: false);

    public static FakeParamsValidator InvalidWith(params ValidationError[] errors) =>
        new(isValid: false, errors: errors);
}

// ── InMemoryIdempotency ──────────────────────────────────────────────────────

public sealed class InMemoryIdempotency : IIdempotencyService
{
    private readonly ConcurrentDictionary<string, bool> _claimed = new();

    public Task<bool> TryClaimAsync(string tenantId, string requestId,
        TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult(_claimed.TryAdd($"{tenantId}:{requestId}", true));
}

// ── RecordingBus ─────────────────────────────────────────────────────────────

public sealed class RecordingBus : IOperationBus
{
    public List<(object Message, string RoutingKey)> Published { get; } = [];

    public Task PublishAsync<T>(T message, string routingKey, CancellationToken ct = default)
        where T : class
    {
        Published.Add((message!, routingKey));
        return Task.CompletedTask;
    }
}

// ── RecordingCancelBus ───────────────────────────────────────────────────────

public sealed class RecordingCancelBus : ICancelBus
{
    public List<(string RequestId, string UserId, string TenantId)> Calls { get; } = [];

    public Task PublishCancelAsync(string requestId, string userId, string tenantId,
        CancellationToken ct = default)
    {
        Calls.Add((requestId, userId, tenantId));
        return Task.CompletedTask;
    }
}

// ── FakeDatabase ─────────────────────────────────────────────────────────────

/// <summary>
/// Test double for <see cref="IDatabase"/> backed by NSubstitute + in-memory dictionaries.
/// Only the operations exercised by Phase 7 production code are given real behaviour.
/// Everything else either delegates to NSubstitute stubs (no-op) or is tracked.
/// </summary>
public sealed class FakeDatabase
{
    // The substituted IDatabase that production code receives
    public IDatabase Inner { get; }

    // ── Internal storage ─────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, string>          _strings = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _sets    = new();
    private readonly ConcurrentDictionary<string, StreamEntry[]>   _streams = new();

    // ── Tracking ─────────────────────────────────────────────────────────
    public List<string> PublishedChannels { get; } = [];
    public List<string> SetRemoved        { get; } = [];
    public List<string> StringKeys        { get; } = [];

    public FakeDatabase()
    {
        Inner = Substitute.For<IDatabase>();

        // ── StringSetAsync ──────────────────────────────────────────────
        Inner.StringSetAsync(
                Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<bool>(),
                Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key   = ((RedisKey)ci[0]!).ToString()!;
                var value = (string?)((RedisValue)ci[1]) ?? string.Empty;
                StringKeys.Add(key);
                _strings[key] = value;
                return Task.FromResult(true);
            });

        // ── StringGetAsync ──────────────────────────────────────────────
        Inner.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = ((RedisKey)ci[0]!).ToString()!;
                return Task.FromResult(_strings.TryGetValue(key, out var v)
                    ? (RedisValue)v
                    : RedisValue.Null);
            });

        // ── KeyExistsAsync ──────────────────────────────────────────────
        Inner.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = ((RedisKey)ci[0]!).ToString()!;
                return Task.FromResult(_strings.ContainsKey(key));
            });

        // ── KeyDeleteAsync ──────────────────────────────────────────────
        Inner.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = ((RedisKey)ci[0]!).ToString()!;
                return Task.FromResult(_strings.TryRemove(key, out _));
            });

        // ── SetAddAsync ─────────────────────────────────────────────────
        Inner.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key    = ((RedisKey)ci[0]!).ToString()!;
                var member = (string?)((RedisValue)ci[1]) ?? string.Empty;
                var set    = _sets.GetOrAdd(key, _ => []);
                lock (set) { set.Add(member); }
                return Task.FromResult(true);
            });

        // ── SetRemoveAsync ──────────────────────────────────────────────
        Inner.SetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var member = (string?)((RedisValue)ci[1]) ?? string.Empty;
                SetRemoved.Add(member);
                var key = ((RedisKey)ci[0]!).ToString()!;
                if (_sets.TryGetValue(key, out var set))
                    lock (set) { return Task.FromResult(set.Remove(member)); }
                return Task.FromResult(false);
            });

        // ── SetMembersAsync ─────────────────────────────────────────────
        Inner.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = ((RedisKey)ci[0]!).ToString()!;
                if (!_sets.TryGetValue(key, out var set))
                    return Task.FromResult(Array.Empty<RedisValue>());
                lock (set)
                    return Task.FromResult(set.Select(s => (RedisValue)s).ToArray());
            });

        // ── PublishAsync ────────────────────────────────────────────────
        Inner.PublishAsync(Arg.Any<RedisChannel>(), Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                PublishedChannels.Add(((RedisChannel)ci[0]).ToString());
                return Task.FromResult(0L);
            });

        // ── StreamReadAsync ─────────────────────────────────────────────
        Inner.StreamReadAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<int?>(), Arg.Any<CommandFlags>())
            .Returns(ci =>
            {
                var key = ((RedisKey)ci[0]!).ToString()!;
                return Task.FromResult(_streams.TryGetValue(key, out var e) ? e : Array.Empty<StreamEntry>());
            });

        // ── KeyExpireAsync ──────────────────────────────────────────────
        Inner.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(true));

        // ── StreamAddAsync ──────────────────────────────────────────────
        Inner.StreamAddAsync(
                Arg.Any<RedisKey>(), Arg.Any<NameValueEntry[]>(),
                Arg.Any<RedisValue?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);
    }

    // ── Convenience helpers ───────────────────────────────────────────────

    /// <summary>Pre-populate a string key (used by OrphanDetector tests).</summary>
    public void StringSet(string key, string value) => _strings[key] = value;

    /// <summary>Pre-populate stream entries for ProgressRingBuffer tests.</summary>
    public void SetStreamEntries(string key, StreamEntry[] entries) => _streams[key] = entries;

    /// <summary>Check whether a key exists in the in-memory string dict.</summary>
    public bool ContainsKey(string key) => _strings.ContainsKey(key);

    /// <summary>Check whether a member exists in the in-memory set.</summary>
    public bool SetContains(string key, string member)
    {
        if (!_sets.TryGetValue(key, out var set)) return false;
        lock (set) return set.Contains(member);
    }
}

// ── RecordingHubClient ───────────────────────────────────────────────────────

/// <summary>Records all IMainHubClient method calls for assertion.</summary>
public sealed class RecordingHubClient : IMainHubClient
{
    public List<ResponseDispatchPushMessage> Completed  { get; } = [];
    public List<ResponseDispatchPushMessage> Failed     { get; } = [];
    public List<ResponseDispatchPushMessage> Cancelled  { get; } = [];

    public Task RequestCompleted(ResponseDispatchPushMessage push)  { Completed.Add(push);  return Task.CompletedTask; }
    public Task RequestFailed(ResponseDispatchPushMessage push)     { Failed.Add(push);     return Task.CompletedTask; }
    public Task RequestCancelled(ResponseDispatchPushMessage push)  { Cancelled.Add(push);  return Task.CompletedTask; }
    public Task WidgetStale(string channel, WidgetStaleHint hint)   => Task.CompletedTask;

    public int TotalCalls => Completed.Count + Failed.Count + Cancelled.Count;
}

// ── RecordingHubClients ──────────────────────────────────────────────────────

/// <summary>
/// Fake IHubClients&lt;IMainHubClient&gt;.
/// Tracks which connectionIds and groups received pushes.
/// </summary>
public sealed class RecordingHubClients : IHubClients<IMainHubClient>
{
    private readonly ConcurrentDictionary<string, RecordingHubClient> _clients = new();
    private readonly ConcurrentDictionary<string, RecordingHubClient> _groups  = new();

    public RecordingHubClient GetOrAddClient(string connectionId) =>
        _clients.GetOrAdd(connectionId, _ => new RecordingHubClient());

    public RecordingHubClient GetOrAddGroup(string group) =>
        _groups.GetOrAdd(group, _ => new RecordingHubClient());

    public List<string> ClientTargets { get; } = [];
    public List<string> GroupTargets  { get; } = [];

    public IMainHubClient All                                                        => new RecordingHubClient();
    public IMainHubClient AllExcept(IReadOnlyList<string> excludedConnectionIds)     => new RecordingHubClient();
    public IMainHubClient Client(string connectionId)   { ClientTargets.Add(connectionId); return GetOrAddClient(connectionId); }
    public IMainHubClient Clients(IReadOnlyList<string> connectionIds)               => new RecordingHubClient();
    public IMainHubClient Group(string groupName)       { GroupTargets.Add(groupName);     return GetOrAddGroup(groupName); }
    public IMainHubClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new RecordingHubClient();
    public IMainHubClient Groups(IReadOnlyList<string> groupNames)                   => new RecordingHubClient();
    public IMainHubClient User(string userId)                                        => new RecordingHubClient();
    public IMainHubClient Users(IReadOnlyList<string> userIds)                       => new RecordingHubClient();
}

// ── RecordingHubContext ──────────────────────────────────────────────────────

/// <summary>Fake IHubContext&lt;MainHub, IMainHubClient&gt; for unit tests.</summary>
public sealed class RecordingHubContext : IHubContext<MainHub, IMainHubClient>
{
    public RecordingHubClients RecordingClients { get; } = new();

    public IHubClients<IMainHubClient> Clients  => RecordingClients;
    public IGroupManager Groups => new NullGroupManager();

    private sealed class NullGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default) => Task.CompletedTask;
    }
}

// ── TestFactories ────────────────────────────────────────────────────────────

public static class TestFactories
{
    public static RequestEnvelope MakeEnvelope(
        string operation   = "test.op",
        string requestId   = "req-1",
        string tenantId    = "tenant-1",
        string userId      = "user-1",
        bool   progress    = false,
        string paramsJson  = "{}") =>
        new()
        {
            RequestId   = requestId,
            Operation   = operation,
            Params      = JsonDocument.Parse(paramsJson).RootElement,
            TenantId    = tenantId,
            UserId      = userId,
            Options     = new RequestOptions { Progress = progress },
        };

    /// <summary>
    /// Build a <see cref="RequestSubmissionService"/> wired with in-memory fakes.
    /// Uses the production (full-deps) constructor so owner-store + Redis side-effects fire.
    /// </summary>
    public static (RequestSubmissionService Svc, FakeDatabase Db, RecordingBus Bus)
        MakeSubmissionService(
            InMemoryIdempotency?    idempotency = null,
            FakeOperationRegistry?  registry    = null,
            FakeParamsValidator?    validator   = null)
    {
        var db         = new FakeDatabase();
        var ownerStore = new OwnerStore(db.Inner);
        var bus        = new RecordingBus();
        var svc        = new RequestSubmissionService(
            registry    ?? FakeOperationRegistry.Active(),
            validator   ?? FakeParamsValidator.AlwaysValid(),
            idempotency ?? new InMemoryIdempotency(),
            bus,
            ownerStore,
            db.Inner,
            NullLogger<RequestSubmissionService>.Instance);
        return (svc, db, bus);
    }

    public static OwnerStore BuildOwnerStore(FakeDatabase db) => new(db.Inner);

    public static ResultStore BuildResultStore(FakeDatabase db) => new(db.Inner);

    public static ResponseRouter MakeRouter(
        RecordingHubContext hub,
        OwnerStore owners,
        ResultStore results,
        FakeDatabase db,
        DispatcherOptions? opts = null) =>
        new(hub,
            owners,
            results,
            db.Inner,
            Options.Create(opts ?? new DispatcherOptions()),
            NullLogger<ResponseRouter>.Instance);

    public static OperationResponseMessage MakeResponse(
        string requestId = "req-1",
        string tenantId  = "tenant-1",
        string userId    = "user-1",
        ResponseStatus status = ResponseStatus.Done,
        string? operation = "test.op") =>
        new()
        {
            RequestId = requestId,
            TenantId  = tenantId,
            UserId    = userId,
            Status    = status,
            Operation = operation,
        };
}
