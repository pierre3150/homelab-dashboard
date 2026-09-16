using System.Text.Json;

namespace HomelabDashboard.Services;

public interface ISshHostMapService
{
    string? ResolveIp(int vmid);
}

/// <summary>
/// Maps a Proxmox VMID to the IP address our app should SSH into for the
/// console feature. Deliberately NOT auto-discovered from Proxmox (DHCP
/// leases/guest-agent reporting are unreliable across container templates) -
/// configured explicitly via SSH_HOSTS_MAP, a JSON object of {"vmid": "ip"}.
/// </summary>
public class SshHostMapService : ISshHostMapService
{
    private readonly Dictionary<int, string> _map;
    private readonly ILogger<SshHostMapService> _logger;

    public SshHostMapService(IConfiguration configuration, ILogger<SshHostMapService> logger)
    {
        _logger = logger;
        _map = ParseMap(configuration["Ssh:HostsMapJson"]);
    }

    public string? ResolveIp(int vmid) => _map.GetValueOrDefault(vmid);

    private Dictionary<int, string> ParseMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<int, string>();

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            return raw.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Ssh:HostsMapJson - console feature will have no known hosts");
            return new Dictionary<int, string>();
        }
    }
}
