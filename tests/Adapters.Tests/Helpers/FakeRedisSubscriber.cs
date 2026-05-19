using StackExchange.Redis;

namespace ReportingPlatform.Adapters.Tests.Helpers;

/// <summary>
/// NSubstitute-backed ISubscriber that captures the subscribe callback and allows
/// tests to trigger it deterministically via <see cref="Trigger"/>.
/// </summary>
internal sealed class FakeRedisSubscriber
{
    private Action<RedisChannel, RedisValue>? _capturedHandler;

    public ISubscriber Subscriber { get; }

    public FakeRedisSubscriber()
    {
        Subscriber = Substitute.For<ISubscriber>();

        Subscriber
            .When(s => s.SubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<Action<RedisChannel, RedisValue>>(),
                Arg.Any<CommandFlags>()))
            .Do(ci => _capturedHandler = ci.ArgAt<Action<RedisChannel, RedisValue>>(1));

        // UnsubscribeAsync: clear handler (no-op for test correctness — fire-and-forget cleanup)
        Subscriber
            .When(s => s.UnsubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<Action<RedisChannel, RedisValue>?>(),
                Arg.Any<CommandFlags>()))
            .Do(_ => _capturedHandler = null);
    }

    /// <summary>Fires the captured subscription handler as if the Bridge published a terminal notification.</summary>
    public void Trigger(string requestId)
    {
        var channel = RedisChannel.Literal(RedisKeys.SseTerminal(requestId));
        _capturedHandler?.Invoke(channel, RedisValue.EmptyString);
    }
}
