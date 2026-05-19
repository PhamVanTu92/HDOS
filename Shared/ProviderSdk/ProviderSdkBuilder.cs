namespace ReportingPlatform.ProviderSdk;

internal sealed class ProviderSdkBuilder : IProviderSdkBuilder
{
    private readonly Internal.HandlerRegistry _registry;
    private readonly Internal.SdkCallbacks    _callbacks;

    internal ProviderSdkBuilder(Internal.HandlerRegistry registry, Internal.SdkCallbacks callbacks)
    {
        _registry  = registry;
        _callbacks = callbacks;
    }

    public IProviderSdkBuilder Handle<TParams, TResult>(string operation)
        where TParams : class
        where TResult : class
    {
        _registry.Register<TParams, TResult>(operation);
        return this;
    }

    public IProviderSdkBuilder OnConnected(Func<string, Welcome, Task> cb)    { _callbacks.OnConnected = cb; return this; }
    public IProviderSdkBuilder OnConnected(Action<string, Welcome> cb)         { _callbacks.OnConnected = (s, w) => { cb(s, w); return Task.CompletedTask; }; return this; }
    public IProviderSdkBuilder OnDisconnected(Func<string, Task> cb)           { _callbacks.OnDisconnected = cb; return this; }
    public IProviderSdkBuilder OnDisconnected(Action<string> cb)               { _callbacks.OnDisconnected = s => { cb(s); return Task.CompletedTask; }; return this; }
    public IProviderSdkBuilder OnCredentialsRevoked(Func<Task> cb)             { _callbacks.OnCredentialsRevoked = cb; return this; }
    public IProviderSdkBuilder OnCredentialsRevoked(Action cb)                 { _callbacks.OnCredentialsRevoked = () => { cb(); return Task.CompletedTask; }; return this; }
    public IProviderSdkBuilder OnReconnecting(Func<int, TimeSpan, Task> cb)    { _callbacks.OnReconnecting = cb; return this; }
    public IProviderSdkBuilder OnReconnecting(Action<int, TimeSpan> cb)        { _callbacks.OnReconnecting = (i, t) => { cb(i, t); return Task.CompletedTask; }; return this; }
}
