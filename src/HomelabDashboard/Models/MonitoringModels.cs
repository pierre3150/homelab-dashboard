namespace HomelabDashboard.Models;

/// <summary>
/// One periodic measurement of CPU/RAM/disk for a node or a guest (VM/CT),
/// captured by ResourceSamplingService. This is the raw material the morning
/// report averages over - a single dashboard snapshot is an instant, this is
/// the history behind it.
/// </summary>
public class ResourceSample
{
    public int Id { get; set; }
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    /// <summary>"node" or "guest".</summary>
    public string TargetType { get; set; } = string.Empty;

    /// <summary>Proxmox node this measurement came from.</summary>
    public string Node { get; set; } = string.Empty;

    /// <summary>Null for a node sample; the guest's VMID for a guest sample.</summary>
    public int? VmId { get; set; }

    /// <summary>"qemu" (VM) or "lxc" (container) for a guest sample; empty for a node sample.</summary>
    public string GuestType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public double CpuUsage { get; set; }
    public long MemoryUsed { get; set; }
    public long MemoryTotal { get; set; }
    public long DiskUsed { get; set; }
    public long DiskTotal { get; set; }
}

/// <summary>
/// A problem observed at sampling time, kept so the morning report can surface
/// issues that happened overnight even if they've since cleared up (a disk
/// that flickered to FAILED and back, an API timeout at 3am, ...).
/// </summary>
public class DetectedErrorLog
{
    public int Id { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = string.Empty;
    public string Node { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
