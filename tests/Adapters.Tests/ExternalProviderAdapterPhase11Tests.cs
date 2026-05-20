using ReportingPlatform.Adapters.Tests.Helpers;

namespace ReportingPlatform.Adapters.Tests;

/// <summary>EP12–EP13: Phase 10 deferred items — progress forwarding + ProviderId hint.</summary>
public sealed class ExternalProviderAdapterPhase11Tests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (ExternalProviderAdapter Adapter, FakeSubmissionService Submission,
        FakeRedisSubscriberMulti Subscriber, FakeResultStore Store) Build()
    {
        var submission = new FakeSubmissionService();
        var subscriber = new FakeRedisSubscriberMulti();
        var store      = new FakeResultStore();
        var adapter    = new ExternalProviderAdapter(
            submission,
            store,
            subscriber.Subscriber,
            NullLogger<ExternalProviderAdapter>.Instance);
        return (adapter, submission, subscriber, store);
    }

    private static AdapterRequest MakeRequest(
        string? parentRequestId  = null,
        bool parentWantsProgress = false,
        string? providerId       = null)
    {
        var configJson = providerId is not null
            ? $$"""{"operationName":"fraud.score","paramMapping":{},"providerId":"{{providerId}}"}"""
            : """{"operationName":"fraud.score","paramMapping":{}}""";

        return new()
        {
            TenantId         = "t1",
            Datasource       = new DatasourceDefinition
            {
                DatasourceId     = "ds-ep",
                DisplayName      = "EP datasource",
                Type             = "external_provider",
                CacheSeconds     = 0,
                ConnectionConfig = JsonDocument.Parse(configJson).RootElement,
            },
            Filters              = new Dictionary<string, JsonElement>(),
            ParentRequestId      = parentRequestId,
            ParentWantsProgress  = parentWantsProgress,
        };
    }

    private static ResultStoreRecord SuccessRecord(string requestId) =>
        new()
        {
            RequestId   = requestId,
            Status      = ResponseStatus.Done,
            PayloadJson = """{"rows":[]}""",
            TenantId    = "t1",
            StoredAt    = DateTimeOffset.UtcNow,
        };

    // ─── EP12: Progress forwarding ───────────────────────────────────────────

    [Fact]
    public async Task EP12_NestedProgress_WantsProgressTrue_ForwardedToParentChannel()
    {
        var (adapter, submission, subscriber, store) = Build();
        const string parentId = "parent-req-99";

        submission.OnSubmitAsync = async () =>
        {
            // Simulate 2 progress events arriving on the nested notify channel.
            subscriber.TriggerProgress(submission.Captured!.RequestId, "25%");
            subscriber.TriggerProgress(submission.Captured.RequestId, "75%");

            // Then the terminal event.
            store.Seed(SuccessRecord(submission.Captured.RequestId));
            subscriber.TriggerTerminal(submission.Captured.RequestId);

            await Task.CompletedTask;
        };

        await adapter.FetchAsync(MakeRequest(
            parentRequestId: parentId,
            parentWantsProgress: true));

        // Both progress events should have been re-published to the parent notify channel.
        var parentNotifyKey = $"rp:sse-notify:{parentId}";
        var forwardedToParent = subscriber.Published
            .Where(p => ((string)p.Channel!).Equals(parentNotifyKey, StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, forwardedToParent.Count);
        Assert.Contains(forwardedToParent, p => p.Value == "25%");
        Assert.Contains(forwardedToParent, p => p.Value == "75%");
    }

    [Fact]
    public async Task EP12_NestedProgress_WantsProgressFalse_NotForwarded()
    {
        var (adapter, submission, subscriber, store) = Build();
        const string parentId = "parent-req-88";

        submission.OnSubmitAsync = async () =>
        {
            // Progress fires, but ParentWantsProgress=false so no subscription.
            subscriber.TriggerProgress(submission.Captured!.RequestId, "50%");
            store.Seed(SuccessRecord(submission.Captured.RequestId));
            subscriber.TriggerTerminal(submission.Captured.RequestId);
            await Task.CompletedTask;
        };

        await adapter.FetchAsync(MakeRequest(
            parentRequestId: parentId,
            parentWantsProgress: false));

        var parentNotifyKey = $"rp:sse-notify:{parentId}";
        var forwardedToParent = subscriber.Published
            .Where(p => ((string)p.Channel!).Equals(parentNotifyKey, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forwardedToParent);
    }

    // ─── EP13: ProviderId routing hint in envelope ───────────────────────────

    [Fact]
    public async Task EP13_ProviderIdHint_SetInEnvelope_FromConfig()
    {
        var (adapter, submission, subscriber, store) = Build();

        submission.OnSubmitAsync = async () =>
        {
            store.Seed(SuccessRecord(submission.Captured!.RequestId));
            subscriber.TriggerTerminal(submission.Captured.RequestId);
            await Task.CompletedTask;
        };

        await adapter.FetchAsync(MakeRequest(providerId: "fraud-svc-v2"));

        Assert.Equal("fraud-svc-v2", submission.Captured!.ProviderId);
    }

    [Fact]
    public async Task EP13_ProviderIdHint_NoProviderId_EnvelopeHasNull()
    {
        var (adapter, submission, subscriber, store) = Build();

        submission.OnSubmitAsync = async () =>
        {
            store.Seed(SuccessRecord(submission.Captured!.RequestId));
            subscriber.TriggerTerminal(submission.Captured.RequestId);
            await Task.CompletedTask;
        };

        await adapter.FetchAsync(MakeRequest()); // no providerId

        Assert.Null(submission.Captured!.ProviderId);
    }
}
