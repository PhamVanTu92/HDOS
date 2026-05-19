using Grpc.Core;

namespace ReportingPlatform.ProviderBridge.Tests.Helpers;

/// <summary>IAsyncStreamReader backed by a Channel.Reader.</summary>
public sealed class ChannelReaderStream<T> : IAsyncStreamReader<T>
{
    private readonly System.Threading.Channels.ChannelReader<T> _reader;
    private T _current = default!;

    public ChannelReaderStream(System.Threading.Channels.ChannelReader<T> reader) => _reader = reader;

    public T Current => _current;

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        if (await _reader.WaitToReadAsync(cancellationToken))
        {
            if (_reader.TryRead(out var item))
            {
                _current = item;
                return true;
            }
        }
        return false;
    }
}

/// <summary>IServerStreamWriter backed by a Channel.Writer.</summary>
public sealed class ChannelWriterStream<T> : IServerStreamWriter<T>
{
    private readonly System.Threading.Channels.ChannelWriter<T> _writer;

    public ChannelWriterStream(System.Threading.Channels.ChannelWriter<T> writer) => _writer = writer;

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        _writer.TryWrite(message);
        return Task.CompletedTask;
    }

    public Task WriteAsync(T message, CancellationToken cancellationToken)
    {
        _writer.TryWrite(message);
        return Task.CompletedTask;
    }
}

/// <summary>Minimal concrete ServerCallContext for unit tests.</summary>
public sealed class FakeServerCallContext : Grpc.Core.ServerCallContext
{
    protected override string MethodCore                => "/test/Connect";
    protected override string HostCore                  => "localhost";
    protected override string PeerCore                  => "127.0.0.1";
    protected override DateTime DeadlineCore            => DateTime.MaxValue;
    protected override Grpc.Core.Metadata RequestHeadersCore => new Grpc.Core.Metadata();
    protected override CancellationToken CancellationTokenCore => CancellationToken.None;
    protected override Grpc.Core.Metadata ResponseTrailersCore => new Grpc.Core.Metadata();
    protected override Grpc.Core.Status   StatusCore    { get; set; }
    protected override WriteOptions?      WriteOptionsCore { get; set; }
    protected override AuthContext        AuthContextCore  =>
        new("anonymous", new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
        => throw new NotImplementedException();

    protected override Task WriteResponseHeadersAsyncCore(Grpc.Core.Metadata responseHeaders)
        => Task.CompletedTask;
}
