namespace ReportingPlatform.ProviderBridge.Tests.Helpers;

/// <summary>
/// In-memory signing key service backed by a test RSA-2048 keypair.
/// Fast: generates key once, reuses across tests.
/// </summary>
public sealed class FakeSigningKeyService : ISigningKeyService
{
    private readonly RSA              _rsa;
    private readonly RsaSecurityKey   _key;
    private readonly byte[]           _spki;

    public string           ActiveKid => "test-kid-01";
    public RsaSecurityKey   ActiveKey => _key;

    public IReadOnlyList<(string Kid, byte[] PublicKeySpki)> JwksKeys => [(ActiveKid, _spki)];

    public FakeSigningKeyService()
    {
        _rsa  = RSA.Create(2048);
        _spki = _rsa.ExportSubjectPublicKeyInfo();
        _key  = new RsaSecurityKey(_rsa) { KeyId = ActiveKid };
    }

    public Task ReloadAsync(CancellationToken ct = default) => Task.CompletedTask;
}
