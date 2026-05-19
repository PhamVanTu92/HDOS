namespace ReportingPlatform.Auth;

public interface ISigningKeyService
{
    RsaSecurityKey ActiveKey { get; }
    string ActiveKid { get; }
    IReadOnlyList<(string Kid, byte[] PublicKeySpki)> JwksKeys { get; }
    Task ReloadAsync(CancellationToken ct = default);
}
