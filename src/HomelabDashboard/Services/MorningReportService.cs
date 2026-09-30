using HomelabDashboard.Data;
using HomelabDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HomelabDashboard.Services;

public interface IMorningReportService
{
    Task<MorningReport> GenerateAsync(TimeSpan? window = null, CancellationToken ct = default);
}

/// <summary>
/// Builds the "rapport matinal": current disk usage (per node and per physical
/// disk), any errors seen over the report window (live ones plus whatever
/// ResourceSamplingService logged overnight), and average CPU/RAM usage per
/// guest over that same window.
/// </summary>
public class MorningReportService : IMorningReportService
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    private readonly IDashboardService _dashboardService;
    private readonly AppDbContext _db;

    public MorningReportService(IDashboardService dashboardService, AppDbContext db)
    {
        _dashboardService = dashboardService;
        _db = db;
    }

    public async Task<MorningReport> GenerateAsync(TimeSpan? window = null, CancellationToken ct = default)
    {
        var effectiveWindow = window ?? DefaultWindow;
        var generatedAt = DateTime.UtcNow;
        var cutoff = generatedAt - effectiveWindow;

        var snapshot = await _dashboardService.GetSnapshotAsync(ct);

        var nodeDisks = snapshot.Nodes
            .Select(n => new NodeDiskReport(
                Node: n.Node,
                UsedBytes: n.DiskUsed,
                TotalBytes: n.DiskTotal,
                UsedPercent: n.DiskTotal > 0 ? Math.Round(100.0 * n.DiskUsed / n.DiskTotal, 1) : 0
            ))
            .ToList();

        var guestAverages = await ComputeGuestAveragesAsync(cutoff, ct);
        var errors = await CollectErrorsAsync(snapshot.Errors, cutoff, generatedAt, ct);

        return new MorningReport(
            GeneratedAt: generatedAt,
            Window: effectiveWindow,
            NodeDisks: nodeDisks,
            PhysicalDisks: snapshot.Disks,
            Errors: errors,
            GuestAverages: guestAverages
        );
    }

    private async Task<List<GuestAverageUsage>> ComputeGuestAveragesAsync(DateTime cutoff, CancellationToken ct)
    {
        // Pulled into memory rather than aggregated in SQL: sample volume for
        // a homelab (a handful of guests, one row every few minutes) is small
        // enough that grouping in LINQ-to-Objects is simpler and avoids
        // relying on SQLite translating percentage-of-percentage averages.
        var samples = await _db.ResourceSamples
            .AsNoTracking()
            .Where(s => s.TargetType == "guest" && s.CapturedAt >= cutoff)
            .ToListAsync(ct);

        return samples
            .GroupBy(s => (s.Node, s.VmId, s.Name, s.GuestType))
            .Select(g =>
            {
                var memPercents = g.Where(s => s.MemoryTotal > 0)
                    .Select(s => 100.0 * s.MemoryUsed / s.MemoryTotal)
                    .ToList();

                return new GuestAverageUsage(
                    VmId: g.Key.VmId ?? 0,
                    Name: g.Key.Name,
                    Type: g.Key.GuestType,
                    Node: g.Key.Node,
                    AvgCpuUsagePercent: Math.Round(g.Average(s => s.CpuUsage), 1),
                    AvgMemoryUsedPercent: memPercents.Count > 0 ? Math.Round(memPercents.Average(), 1) : 0,
                    SampleCount: g.Count()
                );
            })
            .OrderBy(g => g.Node).ThenBy(g => g.Name)
            .ToList();
    }

    private async Task<List<ErrorOccurrence>> CollectErrorsAsync(
        List<DashboardError> liveErrors, DateTime cutoff, DateTime generatedAt, CancellationToken ct)
    {
        var logged = await _db.DetectedErrorLogs
            .AsNoTracking()
            .Where(e => e.DetectedAt >= cutoff)
            .ToListAsync(ct);

        // Merge the persisted history with whatever the live snapshot just
        // found (covers the gap since the last sampling tick) and collapse
        // repeats of the same problem into one row with a first/last-seen
        // range - a disk stuck in FAILED for a day shouldn't produce 288
        // near-identical lines.
        var all = logged
            .Select(e => (e.Source, e.Node, e.Message, Seen: e.DetectedAt))
            .Concat(liveErrors.Select(e => (e.Source, e.Node, e.Message, Seen: generatedAt)));

        return all
            .GroupBy(e => (e.Source, e.Node, e.Message))
            .Select(g => new ErrorOccurrence(
                Source: g.Key.Source,
                Node: g.Key.Node,
                Message: g.Key.Message,
                FirstSeen: g.Min(e => e.Seen),
                LastSeen: g.Max(e => e.Seen),
                Count: g.Count()
            ))
            .OrderByDescending(e => e.LastSeen)
            .ToList();
    }
}
