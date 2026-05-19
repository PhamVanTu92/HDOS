namespace ReportingPlatform.Gateway.Tests.Dispatcher;

/// <summary>
/// CR1–CR2: Cancel-race resolution tests.
/// Verifies that only one terminal push is delivered regardless of which path
/// (Cancel or Done) publishes its OperationResponseMessage first.
/// </summary>
public sealed class CancelRaceTests
{
    // ── CR1 — Cancel arrives BEFORE operation completes ──────────────────────
    // CancelRequestConsumer synthesises a Cancelled response first.
    // The subsequent Done response from the Router finds no owner record → no push.

    [Fact]
    public async Task CR1_CancelBeforeComplete_OnlyCancelledPushed()
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);

        await owners.SetAsync(new OwnerStoreRecord
        {
            RequestId    = "req-cr1",
            ConnectionId = "conn-cr1",
            UserId       = "user-cr1",
            TenantId     = "t1",
            SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
        });

        var router = TestFactories.MakeRouter(hub, owners, results, db);

        // Path A: CancelRequestConsumer synthesises Cancelled first
        var cancelledMsg = TestFactories.MakeResponse("req-cr1",
            status: ResponseStatus.Cancelled, userId: "user-cr1");
        await router.RouteAsync(cancelledMsg, CancellationToken.None);

        // Path B: Operation.Router.Worker publishes Done (arrives later — owner already deleted)
        var doneMsg = TestFactories.MakeResponse("req-cr1",
            status: ResponseStatus.Done, userId: "user-cr1");
        await router.RouteAsync(doneMsg, CancellationToken.None);

        var client = hub.RecordingClients.GetOrAddClient("conn-cr1");
        Assert.Single(client.Cancelled);          // exactly one cancel push
        Assert.Empty(client.Completed);           // no done push (owner deleted after first)
        Assert.Empty(client.Failed);
    }

    // ── CR2 — Cancel arrives AFTER operation completes ───────────────────────
    // Done response arrives first, owner record is deleted.
    // Subsequent Cancelled response finds no owner → no push; result store overwrite is idempotent.

    [Fact]
    public async Task CR2_CancelAfterComplete_OnlyCompletedPushed()
    {
        var db      = new FakeDatabase();
        var hub     = new RecordingHubContext();
        var owners  = TestFactories.BuildOwnerStore(db);
        var results = TestFactories.BuildResultStore(db);

        await owners.SetAsync(new OwnerStoreRecord
        {
            RequestId    = "req-cr2",
            ConnectionId = "conn-cr2",
            UserId       = "user-cr2",
            TenantId     = "t1",
            SubmittedAt  = DateTimeOffset.UtcNow.ToString("O"),
        });

        var router = TestFactories.MakeRouter(hub, owners, results, db);

        // Path B: Done arrives first (operation completed before cancel was processed)
        var doneMsg = TestFactories.MakeResponse("req-cr2",
            status: ResponseStatus.Done, userId: "user-cr2");
        await router.RouteAsync(doneMsg, CancellationToken.None);

        // Path A: Cancel arrives late (owner already deleted — no second push)
        var cancelledMsg = TestFactories.MakeResponse("req-cr2",
            status: ResponseStatus.Cancelled, userId: "user-cr2");
        await router.RouteAsync(cancelledMsg, CancellationToken.None);

        var client = hub.RecordingClients.GetOrAddClient("conn-cr2");
        Assert.Single(client.Completed);          // exactly one done push
        Assert.Empty(client.Cancelled);           // no cancel push (owner deleted after first)
        Assert.Empty(client.Failed);
    }
}
