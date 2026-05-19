namespace ReportingPlatform.Adapters.Tests;

/// <summary>EP1–EP11: ExternalProviderAdapter unit tests. SI2 deferred to Phase 12.</summary>
public sealed class ExternalProviderAdapterTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static (ExternalProviderAdapter Adapter, FakeSubmissionService Submission,
        FakeRedisSubscriber RedisSubscriber, FakeResultStore Store) Build()
    {
        var submission = new FakeSubmissionService();
        var subscriber = new FakeRedisSubscriber();
        var store      = new FakeResultStore();
        var adapter    = new ExternalProviderAdapter(
            submission,
            store,
            subscriber.Subscriber,
            NullLogger<ExternalProviderAdapter>.Instance);
        return (adapter, submission, subscriber, store);
    }

    private static AdapterRequest MakeRequest(
        string configJson = """{"operationName":"fraud.score","paramMapping":{}}""",
        IReadOnlyDictionary<string, JsonElement>? filters = null,
        string? parentRequestId = null,
        string? userId = null,
        DateTimeOffset? parentDeadline = null) =>
        new()
        {
            TenantId        = "t1",
            Datasource      = new DatasourceDefinition
            {
                DatasourceId     = "ds-ep",
                DisplayName      = "EP datasource",
                Type             = "external_provider",
                CacheSeconds     = 0,
                ConnectionConfig = JsonDocument.Parse(configJson).RootElement,
            },
            Filters         = filters ?? new Dictionary<string, JsonElement>(),
            ParentRequestId = parentRequestId,
            UserId          = userId,
            ParentDeadline  = parentDeadline,
        };

    private static ResultStoreRecord SuccessRecord(string requestId, string payloadJson) =>
        new()
        {
            RequestId = requestId,
            Status    = ResponseStatus.Done,
            PayloadJson = payloadJson,
            TenantId  = "t1",
            StoredAt  = DateTimeOffset.UtcNow,
        };

    // ─── EP1: Happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task EP1_ValidConfig_SuccessPath_ReturnsAdapterResult()
    {
        var (adapter, submission, subscriber, store) = Build();

        submission.OnSubmitAsync = async () =>
        {
            var record = SuccessRecord(submission.Captured!.RequestId,
                """{"rows":[{"score":0.87,"band":"HIGH"}]}""");
            store.Seed(record);
            subscriber.Trigger(submission.Captured.RequestId);
            await Task.CompletedTask;
        };

        var result = await adapter.FetchAsync(MakeRequest());

        Assert.Single(result.Rows);
        Assert.Equal(0.87, result.Rows[0]["score"].GetDouble(), precision: 2);
        Assert.Equal("HIGH", result.Rows[0]["band"].GetString());
    }

    // ─── EP2: Param mapping ─────────────────────────────────────────────────

    [Fact]
    public async Task EP2_ParamMapping_FiltersInterpolated_CorrectParamsJson()
    {
        var (adapter, submission, subscriber, store) = Build();

        var config = """
            {
              "operationName": "fraud.score",
              "paramMapping": {
                "transactionId": "{{filters.tx_id}}",
                "amount":        "{{filters.amount}}",
                "staticField":   "fixed-value",
                "missing":       "{{filters.not_present}}"
              }
            }
            """;

        var filters = new Dictionary<string, JsonElement>
        {
            ["tx_id"]  = JsonDocument.Parse("\"abc-123\"").RootElement,
            ["amount"] = JsonDocument.Parse("99.5").RootElement,
        };

        submission.OnSubmitAsync = async () =>
        {
            store.Seed(SuccessRecord(submission.Captured!.RequestId, """{"rows":[]}"""));
            subscriber.Trigger(submission.Captured.RequestId);
            await Task.CompletedTask;
        };

        await adapter.FetchAsync(MakeRequest(configJson: config, filters: filters));

        var paramsEl = submission.Captured!.Params;
        Assert.Equal("abc-123", paramsEl.GetProperty("transactionId").GetString());
        Assert.Equal(99.5, paramsEl.GetProperty("amount").GetDouble(), precision: 3);
        Assert.Equal("fixed-value", paramsEl.GetProperty("staticField").GetString());
        Assert.Equal(JsonValueKind.Null, paramsEl.GetProperty("missing").ValueKind);
    }

    // ─── EP3: Timeout ───────────────────────────────────────────────────────

    [Fact]
    public async Task EP3_Timeout_NoNotification_ThrowsAdapterException_PROVIDER_TIMEOUT()
    {
        var (adapter, _, _, _) = Build();
        // No notification triggered; timeout set via config (override with very short value).
        var config = """{"operationName":"fraud.score","paramMapping":{},"timeoutMs":50}""";

        var ex = await Assert.ThrowsAsync<AdapterException>(() =>
            adapter.FetchAsync(MakeRequest(configJson: config)));

        Assert.Equal("PROVIDER_TIMEOUT", ex.ErrorCode);
    }

    // ─── EP4: CancellationToken cancelled ───────────────────────────────────

    [Fact]
    public async Task EP4_CancellationToken_Cancelled_Rethrows()
    {
        var (adapter, _, _, _) = Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.FetchAsync(MakeRequest(), cts.Token));
    }

    // ─── EP5: Result missing ────────────────────────────────────────────────

    [Fact]
    public async Task EP5_ResultStoreMissing_ThrowsAdapterException_PROVIDER_RESULT_MISSING()
    {
        var (adapter, submission, subscriber, _) = Build();
        // Trigger notification but don't seed the store.
        submission.OnSubmitAsync = async () =>
        {
            subscriber.Trigger(submission.Captured!.RequestId);
            await Task.CompletedTask;
        };

        var ex = await Assert.ThrowsAsync<AdapterException>(() =>
            adapter.FetchAsync(MakeRequest()));

        Assert.Equal("PROVIDER_RESULT_MISSING", ex.ErrorCode);
    }

    // ─── EP6: Provider returned FAILED ──────────────────────────────────────

    [Fact]
    public async Task EP6_ProviderFailed_StatusFailed_ThrowsAdapterException_PROVIDER_FAILED()
    {
        var (adapter, submission, subscriber, store) = Build();

        submission.OnSubmitAsync = async () =>
        {
            store.Seed(new ResultStoreRecord
            {
                RequestId = submission.Captured!.RequestId,
                Status    = ResponseStatus.Failed,
                Error     = new ErrorDetail { Code = "RESOURCE_UNAVAILABLE", Message = "Model down" },
                TenantId  = "t1",
                StoredAt  = DateTimeOffset.UtcNow,
            });
            subscriber.Trigger(submission.Captured.RequestId);
            await Task.CompletedTask;
        };

        var ex = await Assert.ThrowsAsync<AdapterException>(() =>
            adapter.FetchAsync(MakeRequest()));

        Assert.Equal("PROVIDER_FAILED", ex.ErrorCode);
    }

    // ─── EP7: No active provider — Bridge returns FAILED(NO_PROVIDER_AVAILABLE) ─

    [Fact]
    public async Task EP7_OperationRegistered_ButNoActiveProvider_FailedWithProviderUnavailable()
    {
        var (adapter, submission, subscriber, store) = Build();

        submission.OnSubmitAsync = async () =>
        {
            store.Seed(new ResultStoreRecord
            {
                RequestId = submission.Captured!.RequestId,
                Status    = ResponseStatus.Failed,
                Error     = new ErrorDetail { Code = "NO_PROVIDER_AVAILABLE", Message = "No provider connected" },
                TenantId  = "t1",
                StoredAt  = DateTimeOffset.UtcNow,
            });
            subscriber.Trigger(submission.Captured.RequestId);
            await Task.CompletedTask;
        };

        var ex = await Assert.ThrowsAsync<AdapterException>(() =>
            adapter.FetchAsync(MakeRequest()));

        Assert.Equal("PROVIDER_FAILED", ex.ErrorCode);
        Assert.Contains("NO_PROVIDER_AVAILABLE", ex.Message);
    }

    // ─── EP8: rowsPath extraction ────────────────────────────────────────────

    [Fact]
    public async Task EP8_RowsPath_NestedExtraction_CorrectRows()
    {
        var (adapter, submission, subscriber, store) = Build();

        var config = """
            {
              "operationName": "fraud.score",
              "paramMapping": {},
              "rowsPath": "data"
            }
            """;

        submission.OnSubmitAsync = async () =>
        {
            store.Seed(SuccessRecord(submission.Captured!.RequestId,
                """{"data":[{"col":"a"},{"col":"b"}],"total":2}"""));
            subscriber.Trigger(submission.Captured.RequestId);
            await Task.CompletedTask;
        };

        var result = await adapter.FetchAsync(MakeRequest(configJson: config));

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("a", result.Rows[0]["col"].GetString());
        Assert.Equal("b", result.Rows[1]["col"].GetString());
    }

    // ─── EP9: Subscribe BEFORE submit — no race ──────────────────────────────

    [Fact]
    public async Task EP9_SubscribeBeforeSubmit_NoRace_NotificationNotMissed()
    {
        // The notification fires synchronously INSIDE SubmitAsync (before WaitAsync is reached).
        // If subscribe happened AFTER submit this would time out.
        var (adapter, submission, subscriber, store) = Build();

        submission.OnSubmitAsync = async () =>
        {
            // At this point the adapter has already subscribed.
            store.Seed(SuccessRecord(submission.Captured!.RequestId, """{"rows":[{"ok":true}]}"""));
            subscriber.Trigger(submission.Captured.RequestId);
            await Task.CompletedTask;
        };

        var result = await adapter.FetchAsync(MakeRequest());

        Assert.Single(result.Rows);
    }

    // ─── EP10: CorrelationId propagated ─────────────────────────────────────

    [Fact]
    public async Task EP10_CorrelationId_PropagatedFromParentRequestId()
    {
        var (adapter, submission, subscriber, store) = Build();

        submission.OnSubmitAsync = async () =>
        {
            store.Seed(SuccessRecord(submission.Captured!.RequestId, """{"rows":[]}"""));
            subscriber.Trigger(submission.Captured.RequestId);
            await Task.CompletedTask;
        };

        await adapter.FetchAsync(MakeRequest(parentRequestId: "parent-req-42"));

        Assert.Equal("parent-req-42", submission.Captured!.CorrelationId);
    }

    // ─── EP11: Operation deleted mid-render ─────────────────────────────────

    [Fact]
    public async Task EP11_OperationDeletedMidRender_OperationNotFound_WrappedAsAdapterException()
    {
        var (adapter, submission, _, _) = Build();

        submission.ThrowOnSubmit = new OperationException("OPERATION_NOT_FOUND",
            "Operation 'fraud.score' was not found.");

        var ex = await Assert.ThrowsAsync<AdapterException>(() =>
            adapter.FetchAsync(MakeRequest()));

        Assert.Equal("PROVIDER_OPERATION_NOT_FOUND", ex.ErrorCode);
    }

    // ─── SI2: Integration (deferred) ────────────────────────────────────────

    [Fact(Skip = "Requires Docker (Testcontainers) — enable in Phase 12")]
    public Task SI2_Integration_RealBridgeAndProvider_EndToEnd()
        => Task.CompletedTask;
}
