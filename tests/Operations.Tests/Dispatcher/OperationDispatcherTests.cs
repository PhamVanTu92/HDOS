using ReportingPlatform.Operations.Tests.Helpers;

namespace ReportingPlatform.Operations.Tests.Dispatcher;

public sealed class OperationDispatcherTests
{
    // ------------------------------------------------------------------
    // Builder helpers
    // ------------------------------------------------------------------

    private static OperationDispatcher MakeDispatcher(
        IOperationHandler? handler = null,
        FakeParamsValidator? validator = null,
        RecordingProgressBuffer? buffer = null)
    {
        var handlers = handler is not null
            ? new[] { handler }
            : Array.Empty<IOperationHandler>();

        return new OperationDispatcher(
            new OperationHandlerRegistry(handlers),
            validator ?? FakeParamsValidator.AlwaysValid(),
            buffer ?? new RecordingProgressBuffer(),
            NullLogger<OperationDispatcher>.Instance);
    }

    private static OperationRequestMessage MakeMessage(
        string operation = "test.op",
        string paramsJson = "{}",
        bool wantsProgress = false,
        long? timeoutAtUnixMs = null) =>
        new()
        {
            RequestId       = "req-1",
            Operation       = operation,
            ParamsJson      = paramsJson,
            TenantId        = "t1",
            UserId          = "u1",
            TimeoutAtUnixMs = timeoutAtUnixMs ?? DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(),
            WantsProgress   = wantsProgress,
            Traceparent     = string.Empty,
        };

    // ------------------------------------------------------------------
    // Dispatch_UnknownOperation_ReturnsHandlerNotFound
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_UnknownOperation_ReturnsHandlerNotFound()
    {
        var dispatcher = MakeDispatcher();  // no handlers registered

        var response = await dispatcher.DispatchAsync(MakeMessage("unknown.op"));

        Assert.Equal(ResponseStatus.Failed, response.Status);
        Assert.Equal("HANDLER_NOT_FOUND", response.Error?.Code);
    }

    // ------------------------------------------------------------------
    // Dispatch_InvalidParams_ReturnsValidationError
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_InvalidParams_ReturnsValidationError()
    {
        var handler    = FakeOperationHandler.Success("test.op", new { ok = true });
        var validator  = FakeParamsValidator.AlwaysInvalid();
        var dispatcher = MakeDispatcher(handler, validator);

        var response = await dispatcher.DispatchAsync(MakeMessage());

        Assert.Equal(ResponseStatus.Failed, response.Status);
        Assert.Equal("VALIDATION_ERROR", response.Error?.Code);
    }

    // ------------------------------------------------------------------
    // Dispatch_OperationException_ReturnsFailed
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_OperationException_ReturnsFailed()
    {
        var handler = FakeOperationHandler.Throws("test.op",
            new OperationException("CUSTOM_CODE", "custom message"));
        var dispatcher = MakeDispatcher(handler);

        var response = await dispatcher.DispatchAsync(MakeMessage());

        Assert.Equal(ResponseStatus.Failed, response.Status);
        Assert.Equal("CUSTOM_CODE", response.Error?.Code);
        Assert.Equal("custom message", response.Error?.Message);
    }

    // ------------------------------------------------------------------
    // Dispatch_DeadlineExceeded_ReturnsTimeout
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_DeadlineExceeded_ReturnsTimeout()
    {
        var dispatcher = MakeDispatcher();

        // TimeoutAtUnixMs in the past
        var msg = MakeMessage(timeoutAtUnixMs: DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds());
        var response = await dispatcher.DispatchAsync(msg);

        Assert.Equal(ResponseStatus.Timeout, response.Status);
        Assert.Equal("DEADLINE_EXCEEDED", response.Error?.Code);
    }

    // ------------------------------------------------------------------
    // Dispatch_CancellationDuringHandler_ReturnsTimeout
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_CancellationDuringHandler_ReturnsTimeout()
    {
        var handler    = FakeOperationHandler.Cancels("test.op");
        var dispatcher = MakeDispatcher(handler);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var response  = await dispatcher.DispatchAsync(MakeMessage(), cts.Token);

        Assert.Equal(ResponseStatus.Timeout, response.Status);
        Assert.Equal("OPERATION_TIMEOUT", response.Error?.Code);
    }

    // ------------------------------------------------------------------
    // Dispatch_Success_ReturnsSuccessWithPayload
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_Success_ReturnsSuccessWithPayload()
    {
        var handler    = FakeOperationHandler.Success("test.op", new { value = 42 });
        var dispatcher = MakeDispatcher(handler);

        var response = await dispatcher.DispatchAsync(MakeMessage());

        Assert.Equal(ResponseStatus.Done, response.Status);
        Assert.Null(response.Error);
        Assert.NotNull(response.PayloadJson);
        using var doc = JsonDocument.Parse(response.PayloadJson!);
        Assert.Equal(42, doc.RootElement.GetProperty("value").GetInt32());
    }

    // ------------------------------------------------------------------
    // Dispatch_WantsProgress_DrainBeforeResponse
    // ------------------------------------------------------------------

    [Fact]
    public async Task Dispatch_WantsProgress_DrainBeforeResponse()
    {
        const int EventCount = 2;
        var handler    = FakeOperationHandler.ReportsProgress("test.op", EventCount);
        var buffer     = new RecordingProgressBuffer();
        var dispatcher = MakeDispatcher(handler, buffer: buffer);

        var msg      = MakeMessage(wantsProgress: true);
        var response = await dispatcher.DispatchAsync(msg);

        // Response is Success
        Assert.Equal(ResponseStatus.Done, response.Status);

        // All progress events drained before response returned
        Assert.Equal(EventCount, buffer.Events.Count);
        Assert.Equal(50,  buffer.Events[0].Percent);
        Assert.Equal(100, buffer.Events[1].Percent);
    }
}
