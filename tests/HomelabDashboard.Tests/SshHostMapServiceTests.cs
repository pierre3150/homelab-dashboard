using HomelabDashboard.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HomelabDashboard.Tests;

public class SshHostMapServiceTests
{
    private static SshHostMapService CreateService(string? json)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ssh:HostsMapJson"] = json })
            .Build();

        return new SshHostMapService(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<SshHostMapService>.Instance);
    }

    [Fact]
    public void ResolveIp_KnownVmid_ReturnsConfiguredIp()
    {
        var service = CreateService("""{"100": "192.168.1.71", "107": "192.168.1.40"}""");

        Assert.Equal("192.168.1.71", service.ResolveIp(100));
        Assert.Equal("192.168.1.40", service.ResolveIp(107));
    }

    [Fact]
    public void ResolveIp_UnknownVmid_ReturnsNull()
    {
        var service = CreateService("""{"100": "192.168.1.71"}""");

        Assert.Null(service.ResolveIp(999));
    }

    [Fact]
    public void ResolveIp_NullConfig_ReturnsNullForEverything()
    {
        var service = CreateService(null);

        Assert.Null(service.ResolveIp(100));
    }

    [Fact]
    public void ResolveIp_MalformedJson_DoesNotThrow_ReturnsNull()
    {
        var service = CreateService("not valid json {{{");

        Assert.Null(service.ResolveIp(100));
    }

    [Fact]
    public void ResolveIp_EmptyJsonObject_ReturnsNullForAnyVmid()
    {
        var service = CreateService("{}");

        Assert.Null(service.ResolveIp(100));
    }
}
