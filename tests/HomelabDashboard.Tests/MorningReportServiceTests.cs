using HomelabDashboard.Data;
using HomelabDashboard.Models;
using HomelabDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace HomelabDashboard.Tests;

public class MorningReportServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task GenerateAsync_ComputesNodeDiskUsagePercent_FromLiveSnapshot()
    {
        var mockDashboard = new Mock<IDashboardService>();
        mockDashboard.Setup(d => d.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSnapshot(
                Nodes: [new("pve1", "online", 10, 0, 0, 0, DiskUsed: 250_000_000_000, DiskTotal: 1_000_000_000_000)],
                Guests: [],
                Disks: [],
                Errors: [],
                FetchedAt: DateTime.UtcNow
            ));

        var db = NewDb();
        var service = new MorningReportService(mockDashboard.Object, db);

        var report = await service.GenerateAsync();

        var nodeDisk = Assert.Single(report.NodeDisks);
        Assert.Equal("pve1", nodeDisk.Node);
        Assert.Equal(25.0, nodeDisk.UsedPercent);
    }

    [Fact]
    public async Task GenerateAsync_AveragesGuestUsage_OverSamplesInsideWindow_ExcludingOlderOnes()
    {
        var mockDashboard = new Mock<IDashboardService>();
        mockDashboard.Setup(d => d.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSnapshot([], [], [], [], DateTime.UtcNow));

        var db = NewDb();
        var now = DateTime.UtcNow;

        // Inside the 24h window: cpu 10 and 20 -> average 15.
        db.ResourceSamples.AddRange(
            new ResourceSample { TargetType = "guest", Node = "pve1", VmId = 107, GuestType = "lxc", Name = "jellyfin", CpuUsage = 10, MemoryUsed = 500, MemoryTotal = 1000, CapturedAt = now.AddHours(-1) },
            new ResourceSample { TargetType = "guest", Node = "pve1", VmId = 107, GuestType = "lxc", Name = "jellyfin", CpuUsage = 20, MemoryUsed = 700, MemoryTotal = 1000, CapturedAt = now.AddHours(-2) },
            // Outside the window - must not affect the average.
            new ResourceSample { TargetType = "guest", Node = "pve1", VmId = 107, GuestType = "lxc", Name = "jellyfin", CpuUsage = 99, MemoryUsed = 999, MemoryTotal = 1000, CapturedAt = now.AddHours(-30) }
        );
        await db.SaveChangesAsync();

        var service = new MorningReportService(mockDashboard.Object, db);

        var report = await service.GenerateAsync(TimeSpan.FromHours(24));

        var guest = Assert.Single(report.GuestAverages);
        Assert.Equal(107, guest.VmId);
        Assert.Equal("jellyfin", guest.Name);
        Assert.Equal(15.0, guest.AvgCpuUsagePercent);
        Assert.Equal(60.0, guest.AvgMemoryUsedPercent);
        Assert.Equal(2, guest.SampleCount);
    }

    [Fact]
    public async Task GenerateAsync_MergesLoggedAndLiveErrors_CollapsingRepeatsIntoOneOccurrence()
    {
        var now = DateTime.UtcNow;
        var mockDashboard = new Mock<IDashboardService>();
        mockDashboard.Setup(d => d.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSnapshot(
                [], [], [],
                Errors: [new DashboardError("disk", "pve1", "Disque /dev/sdb : etat SMART \"FAILED\"", now)],
                FetchedAt: now
            ));

        var db = NewDb();
        db.DetectedErrorLogs.AddRange(
            new DetectedErrorLog { Source = "disk", Node = "pve1", Message = "Disque /dev/sdb : etat SMART \"FAILED\"", DetectedAt = now.AddHours(-6) },
            new DetectedErrorLog { Source = "disk", Node = "pve1", Message = "Disque /dev/sdb : etat SMART \"FAILED\"", DetectedAt = now.AddHours(-3) }
        );
        await db.SaveChangesAsync();

        var service = new MorningReportService(mockDashboard.Object, db);

        var report = await service.GenerateAsync();

        var error = Assert.Single(report.Errors);
        Assert.Equal(3, error.Count); // 2 logged + 1 live
        Assert.Equal("pve1", error.Node);
    }

    [Fact]
    public async Task GenerateAsync_ExcludesErrorsOutsideTheWindow()
    {
        var now = DateTime.UtcNow;
        var mockDashboard = new Mock<IDashboardService>();
        mockDashboard.Setup(d => d.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DashboardSnapshot([], [], [], [], now));

        var db = NewDb();
        db.DetectedErrorLogs.Add(new DetectedErrorLog
        {
            Source = "api",
            Node = "pve2",
            Message = "timeout",
            DetectedAt = now.AddDays(-3),
        });
        await db.SaveChangesAsync();

        var service = new MorningReportService(mockDashboard.Object, db);

        var report = await service.GenerateAsync(TimeSpan.FromHours(24));

        Assert.Empty(report.Errors);
    }
}
