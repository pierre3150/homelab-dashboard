using HomelabDashboard.Models;

namespace HomelabDashboard.Services;

public interface IProxmoxClient
{
    Task<List<NodeStatus>> GetNodesAsync(CancellationToken ct = default);
    Task<List<GuestSummary>> GetGuestsAsync(string node, CancellationToken ct = default);
    Task<List<PhysicalDiskStatus>> GetDisksAsync(string node, CancellationToken ct = default);
}
