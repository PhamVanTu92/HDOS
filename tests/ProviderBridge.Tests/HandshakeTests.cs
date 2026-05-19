using Grpc.Core;
using RabbitMQ.Client;
using ReportingPlatform.Bridge.Bridge;
using ReportingPlatform.Bridge.Resilience;
using ReportingPlatform.Provider.V1;
using ReportingPlatform.ProviderBridge.Tests.Helpers;

namespace ReportingPlatform.ProviderBridge.Tests;

/// <summary>HW1–HW5: Hello/Welcome handshake tests.</summary>
public sealed class HandshakeTests
{
    // Test helpers build ClaimsPrincipal for a given providerId.
    private static ClaimsPrincipal BuildClaims(string providerId, string? purpose = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, providerId),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new(JwtRegisteredClaimNames.Exp,
                DateTimeOffset.UtcNow.AddSeconds(900).ToUnixTimeSeconds().ToString()),
        };
        if (purpose is not null) claims.Add(new("purpose", purpose));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ProviderRegistration BuildRegistration(string providerId = "test-provider") =>
        new()
        {
            ProviderId            = providerId,
            DisplayName           = "Test",
            ClientId              = "test-client",
            ClientSecretHash      = "hash",
            Operations            = ["test.op", "test.op2"],
            ChartTypes            = [],
            Transformers          = [],
            TimeoutMs             = 30_000,
            CircuitBreaker        = new CircuitBreakerConfig(),
            Status                = "active",
            MaxConcurrentRequests = 4,
        };

    // Build a mocked IConnection that supports ProviderRequestConsumer.StartAsync.
    private static IConnection BuildFakeRabbit()
    {
        var rabbit  = Substitute.For<IConnection>();
        var channel = Substitute.For<IChannel>();

        rabbit.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
              .Returns(Task.FromResult(channel));

        channel.QueueDeclareAsync(
                    Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>(),
                    Arg.Any<IDictionary<string, object?>?>(), Arg.Any<bool>(), Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
               .Returns(new QueueDeclareOk("q.test", 0, 0));

        channel.QueueBindAsync(
                    Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                    Arg.Any<IDictionary<string, object?>?>(), Arg.Any<bool>(),
                    Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        channel.BasicQosAsync(Arg.Any<uint>(), Arg.Any<ushort>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        channel.BasicConsumeAsync(
                    Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>(),
                    Arg.Any<IDictionary<string, object?>?>(), Arg.Any<IAsyncBasicConsumer>(),
                    Arg.Any<CancellationToken>())
               .Returns("consumer-tag");

        channel.BasicCancelAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        channel.CloseAsync(Arg.Any<ushort>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        return rabbit;
    }

    // ── HW1 — Valid JWT + valid Hello → Welcome sent ──────────────────────────

    [Fact]
    public async Task HW1_ValidHello_WelcomeSent()
    {
        var fromChannel = System.Threading.Channels.Channel.CreateUnbounded<FromProvider>();
        var toChannel   = System.Threading.Channels.Channel.CreateUnbounded<ToProvider>();

        await fromChannel.Writer.WriteAsync(new FromProvider
        {
            Hello = new Hello { ProviderId = "test-provider", SupportedOperations = { "test.op" } }
        });
        fromChannel.Writer.Complete();

        var session = BuildSession("test-provider", BuildRegistration(), fromChannel, toChannel);
        var claims  = BuildClaims("test-provider");

        await session.RunAsync(claims, CancellationToken.None);

        // Check Welcome was sent.
        toChannel.Reader.TryRead(out var firstMsg);
        Assert.NotNull(firstMsg);
        Assert.Equal(ToProvider.MessageOneofCase.Welcome, firstMsg.MessageCase);
        Assert.Equal(4, firstMsg.Welcome.MaxConcurrentRequests);
        Assert.Equal(30, firstMsg.Welcome.HeartbeatIntervalSeconds);
    }

    // ── HW2 — No Hello within 5s → DEADLINE_EXCEEDED ─────────────────────────

    [Fact]
    public async Task HW2_NoHelloWithin5s_DeadlineExceeded()
    {
        var fromChannel = System.Threading.Channels.Channel.CreateUnbounded<FromProvider>();
        var toChannel   = System.Threading.Channels.Channel.CreateUnbounded<ToProvider>();
        // Never write to fromChannel — simulates provider not sending Hello.

        var session = BuildSession("test-provider", BuildRegistration(), fromChannel, toChannel);
        var claims  = BuildClaims("test-provider");

        // The session has an internal 5s hello timeout. Use 8s outer timeout.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var ex = await Assert.ThrowsAsync<RpcException>(
            () => session.RunAsync(claims, cts.Token));
        Assert.Equal(StatusCode.DeadlineExceeded, ex.Status.StatusCode);
    }

    // ── HW3 — Hello.providerId != jwt.sub → UNAUTHENTICATED ─────────────────

    [Fact]
    public async Task HW3_HelloProviderIdMismatch_Unauthenticated()
    {
        var fromChannel = System.Threading.Channels.Channel.CreateUnbounded<FromProvider>();
        var toChannel   = System.Threading.Channels.Channel.CreateUnbounded<ToProvider>();

        await fromChannel.Writer.WriteAsync(new FromProvider
        {
            Hello = new Hello { ProviderId = "different-provider", SupportedOperations = { "test.op" } }
        });
        fromChannel.Writer.Complete();

        var session = BuildSession("test-provider", BuildRegistration(), fromChannel, toChannel);
        var claims  = BuildClaims("test-provider"); // jwt.sub = "test-provider"

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => session.RunAsync(claims, CancellationToken.None));
        Assert.Equal(StatusCode.Unauthenticated, ex.Status.StatusCode);
    }

    // ── HW4 — supportedOperations contains unregistered op → INVALID_ARGUMENT ─

    [Fact]
    public async Task HW4_UnregisteredOperation_InvalidArgument()
    {
        var fromChannel = System.Threading.Channels.Channel.CreateUnbounded<FromProvider>();
        var toChannel   = System.Threading.Channels.Channel.CreateUnbounded<ToProvider>();

        await fromChannel.Writer.WriteAsync(new FromProvider
        {
            Hello = new Hello
            {
                ProviderId = "test-provider",
                SupportedOperations = { "test.op", "unregistered.op" }
            }
        });
        fromChannel.Writer.Complete();

        var session = BuildSession("test-provider", BuildRegistration(), fromChannel, toChannel);
        var claims  = BuildClaims("test-provider");

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => session.RunAsync(claims, CancellationToken.None));
        Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
    }

    // ── HW5 — Probe JWT with operations declared → INVALID_ARGUMENT ──────────

    [Fact]
    public async Task HW5_ProbeJwtWithOperations_InvalidArgument()
    {
        var fromChannel = System.Threading.Channels.Channel.CreateUnbounded<FromProvider>();
        var toChannel   = System.Threading.Channels.Channel.CreateUnbounded<ToProvider>();

        await fromChannel.Writer.WriteAsync(new FromProvider
        {
            Hello = new Hello
            {
                ProviderId          = "test-provider",
                SupportedOperations = { "test.op" }
            }
        });
        fromChannel.Writer.Complete();

        var session = BuildSession("test-provider", BuildRegistration(), fromChannel, toChannel);
        var claims  = BuildClaims("test-provider", purpose: "probe");

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => session.RunAsync(claims, CancellationToken.None));
        Assert.Equal(StatusCode.InvalidArgument, ex.Status.StatusCode);
        Assert.Contains("probe", ex.Status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static ProviderSession BuildSession(
        string providerId,
        ProviderRegistration registration,
        System.Threading.Channels.Channel<FromProvider> fromChannel,
        System.Threading.Channels.Channel<ToProvider>   toChannel)
    {
        var fromStream  = new ChannelReaderStream<FromProvider>(fromChannel.Reader);
        var toStream    = new ChannelWriterStream<ToProvider>(toChannel.Writer);
        var redis       = Substitute.For<IConnectionMultiplexer>();
        var rabbit      = BuildFakeRabbit();
        Func<OperationResponseMessage, Task> publisher = _ => Task.CompletedTask;
        var sessionMgr  = new ProviderSessionManager();
        var resilience  = new ProviderResiliencePipeline();

        return new ProviderSession(
            Guid.CreateVersion7().ToString(),
            fromStream, toStream,
            new FakeServerCallContext(),
            registration,
            resilience, sessionMgr,
            redis, rabbit,
            publisher,
            NullLogger.Instance);
    }
}
