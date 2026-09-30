using HomelabDashboard.Models;

namespace HomelabDashboard.Services;

public interface IDashboardService
{
    Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

public class DashboardService : IDashboardService
{
    private readonly IProxmoxClient _proxmox;

    public DashboardService(IProxmoxClient proxmox)
    {
        _proxmox = proxmox;
    }

    // Disk health values Proxmox/smartctl reports as "everything's fine".
    // Anything else (FAILED, PRE-FAIL, UNKNOWN, ...) is worth flagging.
    private static readonly HashSet<string> HealthyDiskStates =
        new(StringComparer.OrdinalIgnoreCase) { "PASSED", "OK" };

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var nodes = await _proxmox.GetNodesAsync(ct);
        var errors = new List<DashboardError>();

        if (nodes.Count == 0)
        {
            return new DashboardSnapshot([], [], [], errors, DateTime.UtcNow);
        }

        var perNodeResults = await Task.WhenAll(nodes.Select(n => FetchNodeDataAsync(n.Node, ct)));

        var allGuests = perNodeResults.SelectMany(r => r.Guests).ToList();
        var allDisks = perNodeResults.SelectMany(r => r.Disks).ToList();
        errors.AddRange(perNodeResults.SelectMany(r => r.Errors));

        foreach (var disk in allDisks.Where(d => !HealthyDiskStates.Contains(d.Health)))
        {
            errors.Add(new DashboardError(
                Source: "disk",
                Node: disk.Node,
                Message: $"Disque {disk.DevPath} ({disk.Model ?? "modele inconnu"}) : etat SMART \"{disk.Health}\"",
                DetectedAt: DateTime.UtcNow
            ));
        }

        return new DashboardSnapshot(nodes, allGuests, allDisks, errors, DateTime.UtcNow);
    }

    private async Task<(List<GuestSummary> Guests, List<PhysicalDiskStatus> Disks, List<DashboardError> Errors)>
        FetchNodeDataAsync(string node, CancellationToken ct)
    {
        var errors = new List<DashboardError>();
        var guests = new List<GuestSummary>();
        var disks = new List<PhysicalDiskStatus>();

        try
        {
            guests = await _proxmox.GetGuestsAsync(node, ct);
        }
        catch (HttpRequestException ex)
        {
            errors.Add(new DashboardError("api", node, $"Impossible de lister les guests : {ex.Message}", DateTime.UtcNow));
        }

        try
        {
            disks = await _proxmox.GetDisksAsync(node, ct);
        }
        catch (HttpRequestException ex)
        {
            errors.Add(new DashboardError("api", node, $"Impossible de lire l'etat des disques : {ex.Message}", DateTime.UtcNow));
        }

        return (guests, disks, errors);
    }
}
