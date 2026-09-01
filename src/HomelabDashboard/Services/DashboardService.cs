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

    public async Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var nodes = await _proxmox.GetNodesAsync(ct);

        var guestLists = await Task.WhenAll(
            nodes.Select(n => _proxmox.GetGuestsAsync(n.Node, ct)));

        var allGuests = guestLists.SelectMany(g => g).ToList();

        return new DashboardSnapshot(nodes, allGuests, DateTime.UtcNow);
    }
}
