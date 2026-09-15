using UAParser;

namespace HomelabDashboard.Services;

public record DeviceInfo(string? Os, string? Browser, string? DeviceFamily);

public interface IDeviceInfoParser
{
    DeviceInfo Parse(string userAgent);
}

public class DeviceInfoParser : IDeviceInfoParser
{
    private readonly Parser _parser = Parser.GetDefault();

    public DeviceInfo Parse(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return new DeviceInfo(null, null, null);

        var client = _parser.Parse(userAgent);

        return new DeviceInfo(
            Os: $"{client.OS.Family} {client.OS.Major}".Trim(),
            Browser: $"{client.UA.Family} {client.UA.Major}".Trim(),
            DeviceFamily: client.Device.Family
        );
    }
}
