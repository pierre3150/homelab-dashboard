using HomelabDashboard.Services;
using Xunit;

namespace HomelabDashboard.Tests;

public class DeviceInfoParserTests
{
    private readonly DeviceInfoParser _parser = new();

    [Fact]
    public void Parse_ChromeOnWindows_ExtractsOsAndBrowser()
    {
        const string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                           "(KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        var info = _parser.Parse(ua);

        Assert.Contains("Windows", info.Os);
        Assert.Contains("Chrome", info.Browser);
    }

    [Fact]
    public void Parse_IPhoneSafari_ExtractsIosAndDevice()
    {
        const string ua = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) " +
                           "AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1";

        var info = _parser.Parse(ua);

        Assert.Contains("iOS", info.Os);
    }

    [Fact]
    public void Parse_EmptyUserAgent_ReturnsNullFieldsWithoutThrowing()
    {
        var info = _parser.Parse("");

        Assert.Null(info.Os);
        Assert.Null(info.Browser);
    }
}
