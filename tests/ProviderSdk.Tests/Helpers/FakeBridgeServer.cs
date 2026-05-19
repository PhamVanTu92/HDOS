using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;

namespace ReportingPlatform.ProviderSdk.Tests.Helpers;

internal sealed class FakeBridgeServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly FakeBridgeServiceImpl _svc;
    public string Address { get; }

    private FakeBridgeServer(WebApplication app, FakeBridgeServiceImpl svc, string address)
    {
        _app    = app;
        _svc    = svc;
        Address = address;
    }

    public static async Task<FakeBridgeServer> StartAsync()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        var svc     = new FakeBridgeServiceImpl();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(IPAddress.Loopback, 0, o => o.Protocols = HttpProtocols.Http2);
        });
        builder.Services.AddSingleton(svc);
        builder.Services.AddGrpc();
        var app = builder.Build();
        app.MapGrpcService<FakeBridgeServiceImpl>();
        await app.StartAsync();
        // Get the actual bound address (port 0 binds to a random port)
        var server  = app.Services.GetRequiredService<IServer>();
        var feature = server.Features.Get<IServerAddressesFeature>()
                      ?? throw new InvalidOperationException("IServerAddressesFeature not available.");
        var address = feature.Addresses.First();
        return new FakeBridgeServer(app, svc, address);
    }

    public Task<FakeBridgeSession> WaitForSessionAsync(CancellationToken ct = default)
        => _svc.WaitForSessionAsync(ct);

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}

internal sealed class FakeBridgeSession
{
    private readonly IServerStreamWriter<ToProvider> _writer;
    private readonly Channel<FromProvider> _received = Channel.CreateUnbounded<FromProvider>();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<FromProvider> ReceivedList { get; } = new();
    public Task CompleteSignal => _done.Task;

    internal FakeBridgeSession(IServerStreamWriter<ToProvider> writer) => _writer = writer;

    internal void OnReceived(FromProvider msg)
    {
        ReceivedList.Add(msg);
        _received.Writer.TryWrite(msg);
    }

    public async Task SendAsync(ToProvider msg, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try { await _writer.WriteAsync(msg, ct); }
        finally { _writeLock.Release(); }
    }

    public async Task<FromProvider> WaitForMessageAsync(
        Func<FromProvider, bool> predicate, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await foreach (var msg in _received.Reader.ReadAllAsync(cts.Token))
        {
            if (predicate(msg)) return msg;
        }
        throw new TimeoutException($"Did not receive expected message within {timeout}");
    }

    public void Complete() => _done.TrySetResult();
}

internal sealed class FakeBridgeServiceImpl : OperationProvider.OperationProviderBase
{
    private readonly Channel<FakeBridgeSession> _sessions = Channel.CreateUnbounded<FakeBridgeSession>();

    public Task<FakeBridgeSession> WaitForSessionAsync(CancellationToken ct)
        => _sessions.Reader.ReadAsync(ct).AsTask();

    public override async Task Connect(
        IAsyncStreamReader<FromProvider> requestStream,
        IServerStreamWriter<ToProvider> responseStream,
        ServerCallContext context)
    {
        var session = new FakeBridgeSession(responseStream);
        await _sessions.Writer.WriteAsync(session);

        var readTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var msg in requestStream.ReadAllAsync(context.CancellationToken))
                    session.OnReceived(msg);
            }
            catch { /* client disconnected */ }
        });

        await Task.WhenAny(readTask, session.CompleteSignal);
    }
}
