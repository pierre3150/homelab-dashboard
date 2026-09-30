namespace HomelabDashboard.Models;

public record NodeStatus(
    string Node,
    string Status,
    double CpuUsage,
    long MemoryUsed,
    long MemoryTotal,
    long Uptime,
    long DiskUsed = 0,
    long DiskTotal = 0
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
    long Uptime,
    long DiskUsed = 0,
    long DiskTotal = 0
);

/// <summary>A physical disk on a node, with its SMART health status.</summary>
public record PhysicalDiskStatus(
    string Node,
    string DevPath,
    string? Model,
    string Health,        // "PASSED", "FAILED", "UNKNOWN", ...
    int? WearoutPercent,  // null for spinning disks (Proxmox reports "N/A")
    long SizeBytes
);

/// <summary>A problem worth flagging in the morning report.</summary>
public record DashboardError(
    string Source,     // "disk", "api", ...
    string Node,
    string Message,
    DateTime DetectedAt
);

public record DashboardSnapshot(
    List<NodeStatus> Nodes,
    List<GuestSummary> Guests,
    List<PhysicalDiskStatus> Disks,
    List<DashboardError> Errors,
    DateTime FetchedAt
);
