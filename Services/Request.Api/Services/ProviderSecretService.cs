using Microsoft.AspNetCore.DataProtection;

namespace ReportingPlatform.RequestApi.Services;

/// <summary>
/// Encrypts / decrypts provider plaintext secrets using ASP.NET Core Data Protection.
/// Keys are persisted to Redis and survive container restarts.
/// </summary>
public sealed class ProviderSecretService
{
    private readonly IDataProtector _protector;

    public ProviderSecretService(IDataProtectionProvider dpProvider)
        => _protector = dpProvider.CreateProtector("ReportingPlatform.ProviderSecret.v1");

    public string Encrypt(string plaintext) => _protector.Protect(plaintext);

    public string Decrypt(string ciphertext) => _protector.Unprotect(ciphertext);

    /// <summary>Generate a cryptographically random bootstrap token (32 bytes → base64url).</summary>
    public static string GenerateBootstrapToken()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
               .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
