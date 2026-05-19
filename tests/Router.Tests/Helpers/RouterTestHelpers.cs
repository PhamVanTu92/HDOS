using ReportingPlatform.Contracts.Responses;
using ReportingPlatform.Contracts.Store;
using ReportingPlatform.Contracts.Validation;
using ReportingPlatform.Operations.Abstractions;
using ReportingPlatform.Operations.Context;
using ReportingPlatform.Operations.Dispatcher;
using ReportingPlatform.Operations.Progress;

namespace ReportingPlatform.Router.Tests.Helpers;

// ---- Fake IParamsValidator ----

internal sealed class AlwaysValidParams : IParamsValidator
{
    public static readonly AlwaysValidParams Instance = new();

    public Task<ReportingPlatform.Contracts.Validation.ValidationResult> ValidateAsync(
        string operation, JsonElement parameters, CancellationToken ct = default) =>
        Task.FromResult(ReportingPlatform.Contracts.Validation.ValidationResult.Success);
}

// ---- Fake IProgressBuffer ----

internal sealed class RecordingProgressBuffer : IProgressBuffer
{
    public List<ProgressEvent> Events { get; } = new();

    public Task AppendAsync(ProgressEvent evt, CancellationToken ct = default)
    {
        Events.Add(evt);
        return Task.CompletedTask;
    }
}

// ---- Fake IOperationHandler ----

internal sealed class FakeHandler : IOperationHandler
{
    private readonly Func<OperationHandlerContext, CancellationToken, Task<JsonElement>> _fn;

    public string OperationName { get; }

    public FakeHandler(string name, Func<OperationHandlerContext, CancellationToken, Task<JsonElement>> fn)
    {
        OperationName = name;
        _fn           = fn;
    }

    public Task<JsonElement> HandleAsync(OperationHandlerContext context, CancellationToken ct = default) =>
        _fn(context, ct);

    // ---- Factories ----

    public static FakeHandler Success(string name, JsonElement payload) =>
        new(name, (_, _) => Task.FromResult(payload));

    public static FakeHandler Throws(string name, Exception ex) =>
        new(name, (_, _) => throw ex);

    public static FakeHandler Cancels(string name) =>
        new(name, async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return default;
        });

    public static FakeHandler ReportsProgress(string name, int eventCount) =>
        new(name, async (ctx, ct) =>
        {
            for (var i = 1; i <= eventCount; i++)
                ctx.Progress?.Report(new ProgressUpdate(i * (100 / eventCount), $"step {i}"));
            await Task.CompletedTask;
            return JsonDocument.Parse("{}").RootElement;
        });
}

// ---- Builder: OperationDispatcher with one handler ----

internal static class DispatcherFactory
{
    public static (OperationDispatcher Dispatcher, RecordingProgressBuffer Buffer) Build(
        params IOperationHandler[] handlers)
    {
        var registry = new OperationHandlerRegistry(handlers);
        var buffer   = new RecordingProgressBuffer();
        var dispatcher = new OperationDispatcher(
            registry,
            AlwaysValidParams.Instance,
            buffer,
            NullLogger<OperationDispatcher>.Instance);

        return (dispatcher, buffer);
    }
}

// ---- OperationRequestMessage builder ----

internal static class MessageFactory
{
    public static OperationRequestMessage Make(
        string operation = "test.op",
        string requestId = "req-1",
        string paramsJson = "{}",
        long? timeoutAtUnixMs = null,
        bool wantsProgress = false) =>
        new()
        {
            RequestId       = requestId,
            Operation       = operation,
            ParamsJson      = paramsJson,
            TenantId        = "t1",
            UserId          = "u1",
            TimeoutAtUnixMs = timeoutAtUnixMs
                ?? DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(),
            WantsProgress   = wantsProgress,
            Traceparent     = string.Empty,
        };
}
