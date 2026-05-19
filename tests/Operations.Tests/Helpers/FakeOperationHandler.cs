namespace ReportingPlatform.Operations.Tests.Helpers;

/// <summary>
/// Configurable fake handler for OperationDispatcher tests.
/// </summary>
internal sealed class FakeOperationHandler : IOperationHandler
{
    private readonly Func<OperationHandlerContext, CancellationToken, Task<JsonElement>> _impl;

    public string OperationName { get; }

    public FakeOperationHandler(string operationName,
        Func<OperationHandlerContext, CancellationToken, Task<JsonElement>> impl)
    {
        OperationName = operationName;
        _impl         = impl;
    }

    public Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default) =>
        _impl(context, ct);

    // --- Factory helpers ---

    public static FakeOperationHandler Success(string name, object payload) =>
        new(name, (_, _) =>
        {
            var json = JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return Task.FromResult(JsonDocument.Parse(json).RootElement);
        });

    public static FakeOperationHandler Throws(string name, OperationException ex) =>
        new(name, (_, _) => throw ex);

    public static FakeOperationHandler Throws(string name, Exception ex) =>
        new(name, (_, _) => throw ex);

    public static FakeOperationHandler ReportsProgress(string name, int eventCount) =>
        new(name, async (ctx, ct) =>
        {
            for (var i = 1; i <= eventCount; i++)
            {
                ctx.Progress?.Report(new ProgressUpdate(i * (100 / eventCount), $"Step {i}"));
                await Task.Yield();
            }
            return JsonDocument.Parse("""{"done":true}""").RootElement;
        });

    public static FakeOperationHandler Cancels(string name) =>
        new(name, async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct); // wait until cancelled
            return JsonDocument.Parse("{}").RootElement;
        });
}
