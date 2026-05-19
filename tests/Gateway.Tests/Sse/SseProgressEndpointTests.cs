using System.IO;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using StackExchange.Redis;

namespace ReportingPlatform.Gateway.Tests.Sse;

/// <summary>
/// SS1–SS5: SSE endpoint tests for <see cref="SseProgressEndpoint"/>.
/// Tests exercise <see cref="SseConnectionRegistry"/> directly and simulate
/// pub/sub fan-out by calling <see cref="SseConnectionRegistry.FanOut"/> manually.
/// </summary>
public sealed class SseProgressEndpointTests
{
    // ── SS1 — 3 progress events + terminal → stream closes ───────────────────

    [Fact]
    public async Task SS1_ProgressEvents_ThenTerminal_StreamCloses()
    {
        var registry   = BuildRegistry();
        var ringBuffer = BuildRingBuffer();
        var cts        = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var ms   = new MemoryStream();
        var ctx        = BuildHttpContext(ms);

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            for (int i = 1; i <= 3; i++)
                registry.FanOut("req-ss1", new SseEvent("progress", $"{{\"percent\":{i * 25}}}"));
            await Task.Delay(20);
            registry.FanOut("req-ss1", new SseEvent("terminal", "{\"requestId\":\"req-ss1\"}"));
        });

        await SseProgressEndpoint.HandleAsync(
            "req-ss1", ctx, registry, ringBuffer,
            NullLogger<SseProgressEndpoint.SseProgressEndpointMarker>.Instance, cts.Token);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Equal(3, CountOccurrences(output, "event: progress"));
        Assert.Equal(1, CountOccurrences(output, "event: terminal"));
    }

    // ── SS2 — Buffered events replayed for late-join SSE ─────────────────────

    [Fact]
    public async Task SS2_BufferedEventsReplayed_ForLateJoin()
    {
        var registry = BuildRegistry();
        var db       = new FakeDatabase();
        // Pre-populate Redis Stream with 2 buffered progress events
        var streamKey = RedisKeys.ProgressStream("req-ss2");
        db.SetStreamEntries(streamKey, [
            new StreamEntry("1-1",
            [
                new NameValueEntry("pct", "20"),
                new NameValueEntry("msg", "Buffered step 1"),
                new NameValueEntry("step", ""),
                new NameValueEntry("ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()),
            ]),
            new StreamEntry("1-2",
            [
                new NameValueEntry("pct", "40"),
                new NameValueEntry("msg", "Buffered step 2"),
                new NameValueEntry("step", ""),
                new NameValueEntry("ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()),
            ]),
        ]);
        var ringBuffer = new ProgressRingBuffer(db.Inner);
        var cts        = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var ms   = new MemoryStream();
        var ctx        = BuildHttpContext(ms);

        // Push terminal shortly after connect so stream closes
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            registry.FanOut("req-ss2", new SseEvent("terminal", "{\"requestId\":\"req-ss2\"}"));
        });

        await SseProgressEndpoint.HandleAsync(
            "req-ss2", ctx, registry, ringBuffer,
            NullLogger<SseProgressEndpoint.SseProgressEndpointMarker>.Instance, cts.Token);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        // 2 replayed events from ring buffer
        Assert.Equal(2, CountOccurrences(output, "event: progress"));
        Assert.Equal(1, CountOccurrences(output, "event: terminal"));
    }

    // ── SS3 — No progress events → terminal only ─────────────────────────────

    [Fact]
    public async Task SS3_NoProgressEvents_TerminalOnly()
    {
        var registry   = BuildRegistry();
        var ringBuffer = BuildRingBuffer();
        var cts        = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var ms   = new MemoryStream();
        var ctx        = BuildHttpContext(ms);

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            registry.FanOut("req-ss3", new SseEvent("terminal", "{\"requestId\":\"req-ss3\"}"));
        });

        await SseProgressEndpoint.HandleAsync(
            "req-ss3", ctx, registry, ringBuffer,
            NullLogger<SseProgressEndpoint.SseProgressEndpointMarker>.Instance, cts.Token);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.DoesNotContain("event: progress", output);
        Assert.Equal(1, CountOccurrences(output, "event: terminal"));
    }

    // ── SS4 — Registry correctly registers and unregisters writer ────────────

    [Fact]
    public void SS4_Registry_RegisterUnregister_TracksTotalConnections()
    {
        var registry = BuildRegistry();
        var ch       = Channel.CreateBounded<SseEvent>(1);

        Assert.Equal(0, registry.TotalConnections);

        registry.Register("req-ss4", ch.Writer);
        Assert.Equal(1, registry.TotalConnections);

        registry.Unregister("req-ss4", ch.Writer);
        Assert.Equal(0, registry.TotalConnections);
    }

    // ── SS5 — 5 progress events arrive in order; no event after terminal ──────

    [Fact]
    public async Task SS5_EventOrdering_AllProgressBeforeTerminal()
    {
        var registry   = BuildRegistry();
        var ringBuffer = BuildRingBuffer();
        var cts        = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var ms   = new MemoryStream();
        var ctx        = BuildHttpContext(ms);

        var percents = new[] { 20, 40, 60, 80, 99 };

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            foreach (var pct in percents)
                registry.FanOut("req-ss5", new SseEvent("progress", $"{{\"percent\":{pct}}}"));
            await Task.Delay(20);
            // Terminal closes the stream; the endpoint breaks after writing it.
            registry.FanOut("req-ss5", new SseEvent("terminal", "{\"requestId\":\"req-ss5\"}"));
        });

        await SseProgressEndpoint.HandleAsync(
            "req-ss5", ctx, registry, ringBuffer,
            NullLogger<SseProgressEndpoint.SseProgressEndpointMarker>.Instance, cts.Token);

        var output = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Equal(5, CountOccurrences(output, "event: progress"));
        Assert.Equal(1, CountOccurrences(output, "event: terminal"));

        // No progress event appears after the terminal event
        var terminalIdx   = output.LastIndexOf("event: terminal", StringComparison.Ordinal);
        var progressAfter = output.IndexOf("event: progress", terminalIdx, StringComparison.Ordinal);
        Assert.Equal(-1, progressAfter);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SseConnectionRegistry BuildRegistry() =>
        new(NullLogger<SseConnectionRegistry>.Instance);

    /// <summary>Empty ring buffer — no buffered events.</summary>
    private static ProgressRingBuffer BuildRingBuffer() =>
        new(new FakeDatabase().Inner);

    private static HttpContext BuildHttpContext(Stream output)
    {
        var ctx = new DefaultHttpContext();
        ctx.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(output));
        return ctx;
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) != -1)
        {
            count++;
            idx += pattern.Length;
        }
        return count;
    }
}
