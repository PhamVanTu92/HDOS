namespace ReportingPlatform.ProviderSdk.Internal;

/// <summary>Lifecycle callbacks registered via IProviderSdkBuilder. Singleton.</summary>
internal sealed class SdkCallbacks
{
    public Func<string, Welcome, Task>? OnConnected      { get; set; }
    public Func<string, Task>?         OnDisconnected    { get; set; }
    public Func<Task>?                 OnCredentialsRevoked { get; set; }
    public Func<int, TimeSpan, Task>?  OnReconnecting    { get; set; }
}
