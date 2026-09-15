using HomelabDashboard.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HomelabDashboard.Tests;

public class SecretProtectorTests
{
    private static ISecretProtector CreateProtector()
    {
        var services = new ServiceCollection();
        services.AddDataProtection(); // ephemeral in-memory key ring, fine for tests
        var provider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
        return new SecretProtector(provider);
    }

    [Fact]
    public void Protect_ThenUnprotect_RoundTrips()
    {
        var protector = CreateProtector();
        const string secret = "JBSWY3DPEHPK3PXP";

        var protectedValue = protector.Protect(secret);
        var unprotectedValue = protector.Unprotect(protectedValue);

        Assert.Equal(secret, unprotectedValue);
    }

    [Fact]
    public void Protect_NeverStoresPlaintextSecret()
    {
        var protector = CreateProtector();
        const string secret = "JBSWY3DPEHPK3PXP";

        var protectedValue = protector.Protect(secret);

        Assert.DoesNotContain(secret, protectedValue);
    }
}
