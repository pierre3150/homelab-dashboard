using HomelabDashboard.Data;
using HomelabDashboard.Models;

namespace HomelabDashboard.Services;

/// <summary>
/// Periodically snapshots the dashboard (CPU/RAM/disk per node and per guest,
/// plus any errors) and persists it. This is what turns a live dashboard into
/// something that can answer "what was average usage overnight" or "did any
/// disk error fire while nobody was watching" - a single GetSnapshotAsync call
/// only ever sees the current instant.
/// </summary>
public class ResourceSamplingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResourceSamplingService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _retention;

    public ResourceSamplingService(
        IServiceScopeFactory scopeFactory,
        ILogger<ResourceSamplingService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var intervalMinutes = configuration.GetValue<int?>("Sampling:IntervalMinutes") ?? 5;
        var retentionDays = configuration.GetValue<int?>("Sampling:RetentionDays") ?? 7;
        _interval = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes));
        _retention = TimeSpan.FromDays(Math.Max(1, retentionDays));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger the very first sample slightly so it doesn't fire in lockstep
        // with every other startup task competing for the Proxmox API.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(_interval);
        do
        {
            await SampleOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Runs a single sample-and-purge cycle. Public (rather than private) so
    /// tests can exercise the actual persistence logic without waiting on
    /// PeriodicTimer ticks or reflecting into a BackgroundService.
    /// </summary>
    public async Task SampleOnceAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var dashboardService = scope.ServiceProvider.GetRequiredService<IDashboardService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var snapshot = await dashboardService.GetSnapshotAsync(ct);
            var capturedAt = DateTime.UtcNow;

            foreach (var node in snapshot.Nodes)
            {
                db.ResourceSamples.Add(new ResourceSample
                {
                    CapturedAt = capturedAt,
                    TargetType = "node",
                    Node = node.Node,
                    VmId = null,
                    Name = node.Node,
                    CpuUsage = node.CpuUsage,
                    MemoryUsed = node.MemoryUsed,
                    MemoryTotal = node.MemoryTotal,
                    DiskUsed = node.DiskUsed,
                    DiskTotal = node.DiskTotal,
                });
            }

            foreach (var guest in snapshot.Guests.Where(g => g.Status == "running"))
            {
                db.ResourceSamples.Add(new ResourceSample
                {
                    CapturedAt = capturedAt,
                    TargetType = "guest",
                    Node = guest.Node,
                    VmId = guest.VmId,
                    GuestType = guest.Type,
                    Name = guest.Name,
                    CpuUsage = guest.CpuUsage,
                    MemoryUsed = guest.MemoryUsed,
                    MemoryTotal = guest.MemoryTotal,
                    DiskUsed = guest.DiskUsed,
                    DiskTotal = guest.DiskTotal,
                });
            }

            foreach (var error in snapshot.Errors)
            {
                db.DetectedErrorLogs.Add(new DetectedErrorLog
                {
                    DetectedAt = capturedAt,
                    Source = error.Source,
                    Node = error.Node,
                    Message = error.Message,
                });
            }

            var cutoff = capturedAt - _retention;
            db.ResourceSamples.RemoveRange(db.ResourceSamples.Where(s => s.CapturedAt < cutoff));
            db.DetectedErrorLogs.RemoveRange(db.DetectedErrorLogs.Where(e => e.DetectedAt < cutoff));

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A failed sample (Proxmox unreachable, DB hiccup, ...) shouldn't
            // stop the timer - we just miss this one data point and try again
            // next tick.
            _logger.LogError(ex, "Resource sampling tick failed");
        }
    }
}
