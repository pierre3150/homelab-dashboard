namespace HomelabDashboard.Models;

public record NodeDiskReport(
    string Node,
    long UsedBytes,
    long TotalBytes,
    double UsedPercent
);

/// <summary>Average CPU/RAM usage for one guest (VM or CT) over the report window.</summary>
public record GuestAverageUsage(
    int VmId,
    string Name,
    string Type,
    string Node,
    double AvgCpuUsagePercent,
    double AvgMemoryUsedPercent,
    int SampleCount
);

/// <summary>One distinct problem, possibly seen more than once across the report window.</summary>
public record ErrorOccurrence(
    string Source,
    string Node,
    string Message,
    DateTime FirstSeen,
    DateTime LastSeen,
    int Count
);

public record MorningReport(
    DateTime GeneratedAt,
    TimeSpan Window,
    List<NodeDiskReport> NodeDisks,
    List<PhysicalDiskStatus> PhysicalDisks,
    List<ErrorOccurrence> Errors,
    List<GuestAverageUsage> GuestAverages
);
