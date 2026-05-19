namespace ReportingPlatform.ProviderSdk;

/// <summary>Thrown internally when credentials are revoked (5× consecutive 401, or disconnect reason=credentials_revoked). ConnectionManager catches this and fires OnCredentialsRevoked.</summary>
internal sealed class CredentialsRevokedException : Exception
{
    public CredentialsRevokedException() : base("Provider credentials have been revoked.") { }
}
