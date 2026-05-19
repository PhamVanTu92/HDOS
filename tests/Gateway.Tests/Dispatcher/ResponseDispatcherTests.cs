namespace ReportingPlatform.Gateway.Tests.Dispatcher;

/// <summary>
/// RD1–RD7 + MT1–MT2: ResponseRouter routing tests and multi-tab fan-out tests.
/// </summary>
public sealed class ResponseDispatcherTests
{
    // ── RD1 — Response with known connectionId → Client(id) push ─────────────

    [Fact]
    public async Task RD1_KnownConnectionId_PushesToClient()
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);

        // Seed owner record directly via FakeDatabase (JSON round-trip through real OwnerStore)
        await owners.SetAsync(new OwnerStoreRecord
        {
            RequestId    = "req-rd1",
            ConnectionId = "conn-abc",
            UserId       = "user-1",
            TenantId     = "t1",
            SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
        });

        var router = TestFactories.MakeRouter(hub, owners, results, db);
        var msg    = TestFactories.MakeResponse("req-rd1", status: ResponseStatus.Done);

        await router.RouteAsync(msg, CancellationToken.None);

        Assert.Contains("conn-abc", hub.RecordingClients.ClientTargets);
        Assert.DoesNotContain("user:user-1", hub.RecordingClients.GroupTargets);
        Assert.Single(hub.RecordingClients.GetOrAddClient("conn-abc").Completed);
    }

    // ── RD2 — Unknown connectionId falls back to user group ──────────────────

    [Fact]
    public async Task RD2_UnknownConnectionId_FallsBackToUserGroup()
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);

        // Owner record with no connectionId (HTTP submit scenario)
        await owners.SetAsync(new OwnerStoreRecord
        {
            RequestId    = "req-rd2",
            ConnectionId = null, // no connectionId → falls back to user group
            UserId       = "user-2",
            TenantId     = "t1",
            SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
        });

        var router = TestFactories.MakeRouter(hub, owners, results, db,
            new DispatcherOptions { FallbackToUserGroup = true });
        var msg = TestFactories.MakeResponse("req-rd2", userId: "user-2");

        await router.RouteAsync(msg, CancellationToken.None);

        Assert.Contains("user:user-2", hub.RecordingClients.GroupTargets);
        Assert.Single(hub.RecordingClients.GetOrAddGroup("user:user-2").Completed);
    }

    // ── RD3 — No owner record → result stored, no push ───────────────────────

    [Fact]
    public async Task RD3_NoOwnerRecord_ResultStoredNoPush()
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);

        var router = TestFactories.MakeRouter(hub, owners, results, db);
        var msg    = TestFactories.MakeResponse("req-rd3");

        await router.RouteAsync(msg, CancellationToken.None);

        // No push was sent
        Assert.Empty(hub.RecordingClients.ClientTargets);
        Assert.Empty(hub.RecordingClients.GroupTargets);

        // Result was still stored (for GET /result fallback)
        Assert.Contains(db.StringKeys, k => k.Contains("req-rd3"));
    }

    // ── RD4 — Status routing: Done→Completed; Failed→Failed; Cancelled→Cancelled ─

    [Theory]
    [InlineData(ResponseStatus.Done,      1, 0, 0)]
    [InlineData(ResponseStatus.Failed,    0, 1, 0)]
    [InlineData(ResponseStatus.Cancelled, 0, 0, 1)]
    public async Task RD4_StatusMapping_CallsCorrectHubMethod(
        ResponseStatus status,
        int expectedCompleted, int expectedFailed, int expectedCancelled)
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);
        var connId  = $"conn-rd4-{status}";
        var reqId   = $"req-rd4-{status}";

        await owners.SetAsync(new OwnerStoreRecord
        {
            RequestId    = reqId,
            ConnectionId = connId,
            UserId       = "user-4",
            TenantId     = "t1",
            SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
        });

        var router = TestFactories.MakeRouter(hub, owners, results, db);
        var msg    = TestFactories.MakeResponse(reqId, status: status);

        await router.RouteAsync(msg, CancellationToken.None);

        var client = hub.RecordingClients.GetOrAddClient(connId);
        Assert.Equal(expectedCompleted, client.Completed.Count);
        Assert.Equal(expectedFailed,    client.Failed.Count);
        Assert.Equal(expectedCancelled, client.Cancelled.Count);
    }

    // ── RD5 — Result written to ResultStore with correct requestId ───────────

    [Fact]
    public async Task RD5_ResultWrittenToResultStore()
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);

        var router = TestFactories.MakeRouter(hub, owners, results, db);
        var msg    = TestFactories.MakeResponse("req-rd5", status: ResponseStatus.Done);

        await router.RouteAsync(msg, CancellationToken.None);

        // ResultStore uses key pattern rp:result:{requestId}
        var resultKey = RedisKeys.Result("req-rd5");
        Assert.Contains(resultKey, db.StringKeys);
    }

    // ── RD6 — Owner record deleted after routing ─────────────────────────────

    [Fact]
    public async Task RD6_OwnerRecordDeletedAfterRouting()
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);

        await owners.SetAsync(new OwnerStoreRecord
        {
            RequestId    = "req-rd6",
            ConnectionId = "conn-rd6",
            UserId       = "user-6",
            TenantId     = "t1",
            SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
        });

        // Confirm owner record exists before routing
        var before = await owners.GetAsync("req-rd6");
        Assert.NotNull(before);

        var router = TestFactories.MakeRouter(hub, owners, results, db);
        await router.RouteAsync(TestFactories.MakeResponse("req-rd6"), CancellationToken.None);

        // Owner record must be deleted after routing
        var after = await owners.GetAsync("req-rd6");
        Assert.Null(after);
    }

    // ── RD7 — HTTP submit with X-Connection-Id → pushes to specific client ───

    [Fact]
    public async Task RD7_HttpSubmitWithConnectionId_PushesToSpecificClient()
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);

        // HTTP submit sets connectionId via X-Connection-Id header
        await owners.SetAsync(new OwnerStoreRecord
        {
            RequestId    = "req-rd7",
            ConnectionId = "http-conn-xyz",
            UserId       = "user-7",
            TenantId     = "t1",
            SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
        });

        var router = TestFactories.MakeRouter(hub, owners, results, db);
        await router.RouteAsync(TestFactories.MakeResponse("req-rd7"), CancellationToken.None);

        // Must target the specific connectionId
        Assert.Contains("http-conn-xyz", hub.RecordingClients.ClientTargets);
        // Must NOT fall back to user group
        Assert.DoesNotContain("user:user-7", hub.RecordingClients.GroupTargets);
    }

    // ── MT1 — Three connections, connection gone, all 3 receive via user group ─

    [Fact]
    public async Task MT1_ThreeConnections_NoConnectionId_AllReceiveViaGroup()
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);

        // No connectionId → user group fan-out (simulates gone connection)
        await owners.SetAsync(new OwnerStoreRecord
        {
            RequestId    = "req-mt1",
            ConnectionId = null,
            UserId       = "user-multi",
            TenantId     = "t1",
            SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
        });

        var router = TestFactories.MakeRouter(hub, owners, results, db,
            new DispatcherOptions { FallbackToUserGroup = true });
        await router.RouteAsync(TestFactories.MakeResponse("req-mt1", userId: "user-multi"), CancellationToken.None);

        // Group "user:user-multi" received the push
        Assert.Contains("user:user-multi", hub.RecordingClients.GroupTargets);
        // The group client received exactly one Completed call
        var groupClient = hub.RecordingClients.GetOrAddGroup("user:user-multi");
        Assert.Equal(1, groupClient.TotalCalls);
    }

    // ── MT2 — Connection B disconnects; A and C remain; response to group ─────

    [Fact]
    public async Task MT2_DisconnectedConnection_GroupPushSucceeds()
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);

        // Simulate: specific connection is gone, so no ConnectionId stored
        // (In production, if connection B disconnects, the owner record is not updated.
        //  The hub client returns gracefully — no exception. The group fallback fires
        //  when ConnectionId is null, matching the owner record set by HTTP path.)
        await owners.SetAsync(new OwnerStoreRecord
        {
            RequestId    = "req-mt2",
            ConnectionId = null,  // represents gone connection scenario
            UserId       = "user-mt2",
            TenantId     = "t1",
            SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
        });

        var router = TestFactories.MakeRouter(hub, owners, results, db,
            new DispatcherOptions { FallbackToUserGroup = true });

        // Should NOT throw even if B is gone — group push handles it
        await router.RouteAsync(TestFactories.MakeResponse("req-mt2", userId: "user-mt2"), CancellationToken.None);

        Assert.Contains("user:user-mt2", hub.RecordingClients.GroupTargets);
        Assert.Equal(1, hub.RecordingClients.GetOrAddGroup("user:user-mt2").TotalCalls);
    }
}
