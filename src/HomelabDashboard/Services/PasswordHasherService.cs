using HomelabDashboard.Models;
using Microsoft.AspNetCore.Identity;

namespace HomelabDashboard.Services;

public interface IPasswordHasherService
{
    string Hash(string plainPassword);
    bool Verify(User user, string plainPassword);
}

/// <summary>
/// Thin wrapper around Microsoft.AspNetCore.Identity's PasswordHasher - reuses the
/// same battle-tested PBKDF2 implementation ASP.NET Core Identity uses, without
/// pulling in the full Identity/EF store machinery.
/// </summary>
public class PasswordHasherService : IPasswordHasherService
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string plainPassword) => _hasher.HashPassword(null!, plainPassword);

    public bool Verify(User user, string plainPassword)
    {
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, plainPassword);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
