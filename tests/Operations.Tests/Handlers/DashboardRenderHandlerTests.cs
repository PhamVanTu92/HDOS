using ReportingPlatform.Contracts.TableParams;
using ReportingPlatform.Operations.Handlers.Dashboard;
using ReportingPlatform.Resolver.Abstractions;

namespace ReportingPlatform.Operations.Tests.Handlers;

public sealed class DashboardRenderHandlerTests
{
    private static readonly JsonSerializerOptions DeserOpts =
        new() { PropertyNameCaseInsensitive = true };
    // ------------------------------------------------------------------
    // Fake
    // ------------------------------------------------------------------

    private sealed class FakeDashboardResolver : IDashboardResolver
    {
        private readonly DashboardRenderPayload _payload;

        public FakeDashboardResolver(DashboardRenderPayload payload) => _payload = payload;

        public Task<DashboardRenderPayload> RenderAsync(
            string tenantId, string dashboardCode,
            IReadOnlyDictionary<string, JsonElement> filters,
            IReadOnlyDictionary<string, TablePaginationParams>? tableParams = null,
            CancellationToken ct = default) =>
            Task.FromResult(_payload);
    }

    // ------------------------------------------------------------------
    // Builder
    // ------------------------------------------------------------------

    private static DashboardRenderPayload MakePayload(string code) => new()
    {
        DashboardCode  = code,
        Version        = "1",
        RequestId      = "req-1",
        RenderedAt     = DateTimeOffset.UtcNow.ToString("O"),
        Widgets        = [],
        AppliedFilters = new Dictionary<string, JsonElement>(),
        RefreshPolicy  = new RefreshPolicy { Mode = "manual" },
    };

    private static OperationHandlerContext MakeContext(string paramsJson) => new()
    {
        RequestId   = "req-1",
        TenantId    = "t1",
        UserId      = "u1",
        Params      = JsonDocument.Parse(paramsJson).RootElement,
        Traceparent = string.Empty,
    };

    // ------------------------------------------------------------------
    // Render_ValidParams_ReturnsDashboardRenderPayload
    // ------------------------------------------------------------------

    [Fact]
    public async Task Render_ValidParams_ReturnsDashboardRenderPayload()
    {
        var payload  = MakePayload("sales");
        var handler  = new DashboardRenderHandler(new FakeDashboardResolver(payload));
        var ctx      = MakeContext("{\"dashboardCode\":\"sales\"}");

        var result  = await handler.HandleAsync(ctx);
        var rendered = result.Deserialize<DashboardRenderPayload>(DeserOpts)!;

        Assert.Equal("sales", rendered.DashboardCode);
        Assert.Equal("1", rendered.Version);
    }

    // ------------------------------------------------------------------
    // Render_MissingDashboardCode_Throws
    // ------------------------------------------------------------------

    [Fact]
    public async Task Render_MissingDashboardCode_Throws()
    {
        var payload = MakePayload("x");
        var handler = new DashboardRenderHandler(new FakeDashboardResolver(payload));
        var ctx     = MakeContext("{\"dashboardCode\":\"\"}");

        var ex = await Assert.ThrowsAsync<OperationException>(() =>
            handler.HandleAsync(ctx));

        Assert.Equal("INVALID_PARAMS", ex.Code);
    }
}
