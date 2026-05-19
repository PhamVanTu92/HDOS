namespace ReportingPlatform.ProviderSdk;

public interface IProviderSdkBuilder
{
    /// <summary>Register a class-based handler. Must also register IOperationHandler&lt;TParams, TResult&gt; in DI (e.g. services.AddScoped&lt;MyHandler&gt;()).</summary>
    IProviderSdkBuilder Handle<TParams, TResult>(string operation)
        where TParams : class
        where TResult : class;

    IProviderSdkBuilder OnConnected(Func<string, Welcome, Task> callback);
    IProviderSdkBuilder OnConnected(Action<string, Welcome> callback);
    IProviderSdkBuilder OnDisconnected(Func<string, Task> callback);
    IProviderSdkBuilder OnDisconnected(Action<string> callback);
    IProviderSdkBuilder OnCredentialsRevoked(Func<Task> callback);
    IProviderSdkBuilder OnCredentialsRevoked(Action callback);
    IProviderSdkBuilder OnReconnecting(Func<int, TimeSpan, Task> callback);
    IProviderSdkBuilder OnReconnecting(Action<int, TimeSpan> callback);
}
