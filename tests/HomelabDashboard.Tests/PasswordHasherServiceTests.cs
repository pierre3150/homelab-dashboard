using HomelabDashboard.Models;
using HomelabDashboard.Services;
using Xunit;

namespace HomelabDashboard.Tests;

public class PasswordHasherServiceTests
{
    private readonly PasswordHasherService _hasher = new();

    [Fact]
    public void Hash_ThenVerify_WithCorrectPassword_Succeeds()
    {
        var user = new User { PasswordHash = _hasher.Hash("correct-horse-battery-staple") };

        Assert.True(_hasher.Verify(user, "correct-horse-battery-staple"));
    }

    [Fact]
    public void Verify_WithWrongPassword_Fails()
    {
        var user = new User { PasswordHash = _hasher.Hash("correct-horse-battery-staple") };

        Assert.False(_hasher.Verify(user, "wrong-password"));
    }

    [Fact]
    public void Hash_NeverStoresPlaintext()
    {
        var hash = _hasher.Hash("my-secret-password");

        Assert.DoesNotContain("my-secret-password", hash);
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        // Salted hashing: identical passwords must never produce identical
        // hashes, or an attacker could spot which users share a password.
        var hash1 = _hasher.Hash("same-password");
        var hash2 = _hasher.Hash("same-password");

        Assert.NotEqual(hash1, hash2);
    }
}
