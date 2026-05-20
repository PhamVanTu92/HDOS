using ReportingPlatform.Caching;
using StackExchange.Redis;

namespace ReportingPlatform.Adapters.Tests.Helpers;

/// <summary>
/// Multi-channel variant of FakeRedisSubscriber.
/// Captures subscribe handlers per channel key, allowing tests to
/// trigger specific channels (terminal vs progress) independently.
/// Required for EP12 (progress forwarding) which subscribes to two channels.
/// </summary>
internal sealed class FakeRedisSubscriberMulti
{
    private readonly Dictionary<string, Action<RedisChannel, RedisValue>> _handlers = new();
    private readonly List<(RedisChannel Channel, RedisValue Value)> _published = [];

    public ISubscriber Subscriber { get; }
    public IReadOnlyList<(RedisChannel Channel, RedisValue Value)> Published => _published;

    public FakeRedisSubscriberMulti()
    {
        Subscriber = Substitute.For<ISubscriber>();

        Subscriber
            .When(s => s.SubscribeAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<Action<RedisChannel, RedisValue>>(),
                Arg.Any<CommandFlags>()))
            .Do(ci =>
            {
                var channel = ci.ArgAt<RedisChannel>(0);
                var handler = ci.ArgAt<Action<RedisChannel, RedisValue>>(1);
                _handlers[(string)channel!] = handler;
            });

        Subscriber
            .When(s => s.PublishAsync(
                Arg.Any<RedisChannel>(),
                Arg.Any<RedisValue>(),
                Arg.Any<CommandFlags>()))
            .Do(ci =>
            {
                var channel = ci.ArgAt<RedisChannel>(0);
                var value   = ci.ArgAt<RedisValue>(1);
                _published.Add((channel, value));
            });
    }

    /// <summary>Fires the terminal handler for <paramref name="requestId"/>.</summary>
    public void TriggerTerminal(string requestId)
        => Fire(RedisKeys.SseTerminal(requestId));

    /// <summary>Fires the progress (notify) handler for <paramref name="requestId"/>.</summary>
    public void TriggerProgress(string requestId, string payload = "42%")
        => Fire(RedisKeys.SseNotify(requestId), payload);

    private void Fire(string channelKey, string payload = "")
    {
        var ch = RedisChannel.Literal(channelKey);
        if (_handlers.TryGetValue(channelKey, out var h))
            h(ch, payload);
    }
}
