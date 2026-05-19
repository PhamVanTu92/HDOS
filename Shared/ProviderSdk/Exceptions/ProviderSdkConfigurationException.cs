namespace ReportingPlatform.ProviderSdk;

/// <summary>Thrown when the SDK detects a configuration error that cannot be recovered from (e.g. 400 from token endpoint, INVALID_ARGUMENT from Bridge). Host process should not restart.</summary>
public sealed class ProviderSdkConfigurationException : Exception
{
    public ProviderSdkConfigurationException(string message) : base(message) { }
    public ProviderSdkConfigurationException(string message, Exception inner) : base(message, inner) { }
}
