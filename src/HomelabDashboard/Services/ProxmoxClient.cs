using System.Net.Http.Headers;
using HomelabDashboard.Models;

namespace HomelabDashboard.Services;

/// <summary>
/// Talks to the Proxmox VE REST API using an API token (Datacenter -> Permissions -> API Tokens).
/// Never uses the root password: token-based auth can be scoped and revoked independently.
/// </summary>
public class ProxmoxClient : IProxmoxClient
{
    private readonly HttpClient _http;

    public ProxmoxClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;

        var host = configuration["Proxmox:Host"]
            ?? throw new InvalidOperationException("Proxmox:Host is not configured.");
        var tokenId = configuration["Proxmox:TokenId"]
            ?? throw new InvalidOperationException("Proxmox:TokenId is not configured.");
        var tokenSecret = configuration["Proxmox:TokenSecret"]
            ?? throw new InvalidOperationException("Proxmox:TokenSecret is not configured.");

        _http.BaseAddress = new Uri($"https://{host}:8006/api2/json/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("PVEAPIToken", $"{tokenId}={tokenSecret}");
    }

    public async Task<List<NodeStatus>> GetNodesAsync(CancellationToken ct = default)
    {
        var envelope = await _http.GetFromJsonAsync<ProxmoxEnvelope<List<ProxmoxNodeRaw>>>("nodes", ct);
        var nodes = envelope?.Data ?? [];

        return nodes.Select(n => new NodeStatus(
            Node: n.Node,
            Status: n.Status,
            CpuUsage: Math.Round((n.Cpu ?? 0) * 100, 1),
            MemoryUsed: n.Mem ?? 0,
            MemoryTotal: n.MaxMem ?? 0,
            Uptime: n.Uptime ?? 0
        )).ToList();
    }

    public async Task<List<GuestSummary>> GetGuestsAsync(string node, CancellationToken ct = default)
    {
        var vmsTask = _http.GetFromJsonAsync<ProxmoxEnvelope<List<ProxmoxGuestRaw>>>($"nodes/{node}/qemu", ct);
        var lxcTask = _http.GetFromJsonAsync<ProxmoxEnvelope<List<ProxmoxGuestRaw>>>($"nodes/{node}/lxc", ct);
        await Task.WhenAll(vmsTask, lxcTask);

        var guests = new List<GuestSummary>();
        guests.AddRange(MapGuests(vmsTask.Result?.Data, "qemu", node));
        guests.AddRange(MapGuests(lxcTask.Result?.Data, "lxc", node));

        return guests;
    }

    private static IEnumerable<GuestSummary> MapGuests(List<ProxmoxGuestRaw>? raw, string type, string node)
    {
        if (raw is null) yield break;

        foreach (var g in raw)
        {
            yield return new GuestSummary(
                VmId: g.VmId,
                Name: g.Name ?? $"{type}-{g.VmId}",
                Type: type,
                Status: g.Status,
                Node: node,
                CpuUsage: Math.Round((g.Cpu ?? 0) * 100, 1),
                MemoryUsed: g.Mem ?? 0,
                MemoryTotal: g.MaxMem ?? 0,
                Uptime: g.Uptime ?? 0
            );
        }
    }
}
