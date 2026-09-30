using System.Net.Http.Headers;
using System.Text.Json;
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

        // The node list endpoint doesn't include root filesystem usage - that
        // requires a separate per-node call. Only bother for nodes that are
        // actually online; an offline node will just 500/timeout.
        var rootFsByNode = new Dictionary<string, ProxmoxRootFsRaw?>();
        var statusResults = await Task.WhenAll(nodes
            .Where(n => n.Status == "online")
            .Select(async n =>
            {
                try
                {
                    var status = await _http.GetFromJsonAsync<ProxmoxEnvelope<ProxmoxNodeStatusRaw>>(
                        $"nodes/{n.Node}/status", ct);
                    return (n.Node, RootFs: status?.Data?.RootFs);
                }
                catch (HttpRequestException)
                {
                    // Disk usage is a nice-to-have on top of the base snapshot;
                    // don't let one node's failure take down the whole dashboard.
                    return (n.Node, RootFs: (ProxmoxRootFsRaw?)null);
                }
            }));

        foreach (var (node, rootFs) in statusResults)
        {
            rootFsByNode[node] = rootFs;
        }

        return nodes.Select(n =>
        {
            rootFsByNode.TryGetValue(n.Node, out var rootFs);
            return new NodeStatus(
                Node: n.Node,
                Status: n.Status,
                CpuUsage: Math.Round((n.Cpu ?? 0) * 100, 1),
                MemoryUsed: n.Mem ?? 0,
                MemoryTotal: n.MaxMem ?? 0,
                Uptime: n.Uptime ?? 0,
                DiskUsed: rootFs?.Used ?? 0,
                DiskTotal: rootFs?.Total ?? 0
            );
        }).ToList();
    }

    public async Task<List<PhysicalDiskStatus>> GetDisksAsync(string node, CancellationToken ct = default)
    {
        var envelope = await _http.GetFromJsonAsync<ProxmoxEnvelope<List<ProxmoxDiskRaw>>>(
            $"nodes/{node}/disks/list", ct);
        var disks = envelope?.Data ?? [];

        return disks.Select(d => new PhysicalDiskStatus(
            Node: node,
            DevPath: d.DevPath,
            Model: d.Model,
            Health: string.IsNullOrWhiteSpace(d.Health) ? "UNKNOWN" : d.Health,
            WearoutPercent: ParseWearout(d.Wearout),
            SizeBytes: d.Size ?? 0
        )).ToList();
    }

    private static int? ParseWearout(object? wearout)
    {
        // Proxmox returns "N/A" (as a string) for spinning disks that don't
        // report SSD wear, or an integer percentage for SSDs/NVMe.
        if (wearout is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
            return null;
        }
        return null;
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
                Uptime: g.Uptime ?? 0,
                DiskUsed: g.Disk ?? 0,
                DiskTotal: g.MaxDisk ?? 0
            );
        }
    }
}
