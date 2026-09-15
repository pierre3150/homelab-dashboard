using Microsoft.AspNetCore.DataProtection;

namespace HomelabDashboard.Services;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}

/// <summary>
/// Encrypts TOTP secrets at rest using ASP.NET Core's built-in Data Protection API,
/// so a stolen database file alone isn't enough to clone someone's 2FA - the
/// protection key lives outside the DB (see Program.cs key ring persistence path).
/// </summary>
public class SecretProtector : ISecretProtector
{
    private const string Purpose = "HomelabDashboard.TotpSecret.v1";
    private readonly IDataProtector _protector;

    public SecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
