namespace HomelabDashboard.Models;

public record NodeStatus(
    string Node,
    string Status,
    double CpuUsage,
    long MemoryUsed,
    long MemoryTotal,
    long Uptime
);

public record GuestSummary(
    int VmId,
    string Name,
    string Type,      // "qemu" (VM) or "lxc" (container)
    string Status,     // running, stopped, paused
    string Node,        // which Proxmox node this guest runs on
    double CpuUsage,
    long MemoryUsed,
    long MemoryTotal,
    long Uptime
);

public record DashboardSnapshot(
    List<NodeStatus> Nodes,
    List<GuestSummary> Guests,
    DateTime FetchedAt
);
