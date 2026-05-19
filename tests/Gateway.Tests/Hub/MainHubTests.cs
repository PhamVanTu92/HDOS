using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace ReportingPlatform.Gateway.Tests.Hub;

/// <summary>
/// HB1–HB5: Hub-specific tests exercising <see cref="MainHub"/> methods with faked Hub context.
/// </summary>
public sealed class MainHubTests
{
    // ── Hub context factory ──────────────────────────────────────────────────

    private static MainHub MakeHub(
        RecordingCancelBus? cancelBus = null,
        string userId   = "user-hub",
        string tenantId = "tenant-hub",
        string connId   = "conn-1",
        InMemoryIdempotency? idempotency = null)
    {
        var (svc, _, _) = TestFactories.MakeSubmissionService(idempotency: idempotency);
        var hub = new MainHub(svc, cancelBus ?? new RecordingCancelBus(),
            NullLogger<MainHub>.Instance);

        var claims = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("tenant", tenantId),
        ], "Test"));

        var hubCallerContext = new FakeHubCallerContext(connId, claims);
        hub.Context = hubCallerContext;
        hub.Groups  = new FakeGroupManager();
        hub.Clients = new FakeHubCallerClients();

        return hub;
    }

    // ── HB1 — SubscribeWidget with valid channel calls AddToGroupAsync ────────

    [Fact]
    public async Task HB1_SubscribeWidget_ValidChannel_AddsToGroup()
    {
        var groupManager = new FakeGroupManager();
        var hub = MakeHub();
        hub.Groups = groupManager;

        await hub.SubscribeWidgetAsync("widget:dash-1:w1");

        Assert.Contains("widget:dash-1:w1", groupManager.Added);
    }

    // ── HB2 — SubscribeWidget with malformed channel throws HubException ──────

    [Fact]
    public async Task HB2_SubscribeWidget_MalformedChannel_ThrowsHubException()
    {
        var hub = MakeHub();

        var ex = await Assert.ThrowsAsync<HubException>(() =>
            hub.SubscribeWidgetAsync("not-a-widget-channel"));

        Assert.Equal("VALIDATION_ERROR", ex.Message);
    }

    // ── HB3 — UnsubscribeWidget calls RemoveFromGroupAsync ───────────────────

    [Fact]
    public async Task HB3_UnsubscribeWidget_RemovesFromGroup()
    {
        var groupManager = new FakeGroupManager();
        var hub = MakeHub();
        hub.Groups = groupManager;

        await hub.UnsubscribeWidgetAsync("widget:dash-1:w1");

        Assert.Contains("widget:dash-1:w1", groupManager.Removed);
    }

    // ── HB4 — OnConnectedAsync joins user-level group ──────────────────────

    [Fact]
    public async Task HB4_OnConnectedAsync_JoinsUserGroup()
    {
        var groupManager = new FakeGroupManager();
        var hub = MakeHub(userId: "user-42", connId: "conn-42");
        hub.Groups = groupManager;

        await hub.OnConnectedAsync();

        Assert.Contains("user:user-42", groupManager.Added);
    }

    // ── HB5 — CancelRequest calls ICancelBus.PublishCancelAsync ──────────────

    [Fact]
    public async Task HB5_CancelRequest_PublishesToCancelBus()
    {
        var cancelBus = new RecordingCancelBus();
        var hub = MakeHub(cancelBus: cancelBus, userId: "user-5", tenantId: "t-5");

        await hub.CancelRequestAsync("req-to-cancel");

        Assert.Single(cancelBus.Calls);
        var (reqId, uid, tid) = cancelBus.Calls[0];
        Assert.Equal("req-to-cancel", reqId);
        Assert.Equal("user-5", uid);
        Assert.Equal("t-5", tid);
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly string _connectionId;
        private readonly ClaimsPrincipal _user;

        public FakeHubCallerContext(string connectionId, ClaimsPrincipal user)
        {
            _connectionId = connectionId;
            _user         = user;
        }

        public override string ConnectionId => _connectionId;
        public override string? UserIdentifier => _user.FindFirstValue(ClaimTypes.NameIdentifier);
        public override ClaimsPrincipal User => _user;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public List<string> Added   { get; } = [];
        public List<string> Removed { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
        {
            Added.Add(groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken ct = default)
        {
            Removed.Add(groupName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubCallerClients : IHubCallerClients<IMainHubClient>
    {
        private static IMainHubClient Noop() => new RecordingHubClient();
        public IMainHubClient All => Noop();
        public IMainHubClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => Noop();
        public IMainHubClient Caller => Noop();
        public IMainHubClient Client(string connectionId) => Noop();
        public IMainHubClient Clients(IReadOnlyList<string> connectionIds) => Noop();
        public IMainHubClient Group(string groupName) => Noop();
        public IMainHubClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Noop();
        public IMainHubClient Groups(IReadOnlyList<string> groupNames) => Noop();
        public IMainHubClient OthersInGroup(string groupName) => Noop();
        public IMainHubClient Others => Noop();
        public IMainHubClient User(string userId) => Noop();
        public IMainHubClient Users(IReadOnlyList<string> userIds) => Noop();
    }
}
