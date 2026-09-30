using HomelabDashboard.Data;
using HomelabDashboard.Models;
using HomelabDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HomelabDashboard.Tests;

public class ResourceSamplingServiceTests
{
    private static (ResourceSamplingService Service, AppDbContext Db, IServiceScopeFactory Scopes) Build(
        DashboardSnapshot snapshot, IConfiguration? configuration = null)
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));

        var mockDashboard = new Mock<IDashboardService>();
        mockDashboard.Setup(d => d.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        services.AddScoped(_ => mockDashboard.Object);

        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        configuration ??= new ConfigurationBuilder().Build();
        var sampler = new ResourceSamplingService(
            scopeFactory, NullLogger<ResourceSamplingService>.Instance, configuration);

        // A DbContext scoped to the DI container above, used to assert on
        // what the sampler persisted (same in-memory database name).
        var assertDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName).Options);

        return (sampler, assertDb, scopeFactory);
    }

    [Fact]
    public async Task SampleOnceAsync_PersistsOneSamplePerNodeAndRunningGuest()
    {
        var snapshot = new DashboardSnapshot(
            Nodes: [new("pve1", "online", 12, 4_000_000_000, 16_000_000_000, 100)],
            Guests:
            [
                new(107, "jellyfin", "lxc", "running", "pve1", 5, 500_000_000, 2_000_000_000, 3600),
                new(108, "stopped-vm", "qemu", "stopped", "pve1", 0, 0, 4_000_000_000, 0),
            ],
            Disks: [],
            Errors: [],
            FetchedAt: DateTime.UtcNow
        );

        var (sampler, db, _) = Build(snapshot);

        await sampler.SampleOnceAsync();

        var samples = await db.ResourceSamples.ToListAsync();
        Assert.Equal(2, samples.Count); // 1 node + 1 running guest, stopped guest skipped
        Assert.Contains(samples, s => s.TargetType == "node" && s.Node == "pve1");
        Assert.Contains(samples, s => s.TargetType == "guest" && s.Name == "jellyfin" && s.GuestType == "lxc");
        Assert.DoesNotContain(samples, s => s.Name == "stopped-vm");
    }

    [Fact]
    public async Task SampleOnceAsync_PersistsDetectedErrors()
    {
        var snapshot = new DashboardSnapshot(
            [], [], [],
            Errors: [new DashboardError("disk", "pve1", "Disque /dev/sda en echec", DateTime.UtcNow)],
            FetchedAt: DateTime.UtcNow
        );

        var (sampler, db, _) = Build(snapshot);

        await sampler.SampleOnceAsync();

        var logged = Assert.Single(await db.DetectedErrorLogs.ToListAsync());
        Assert.Equal("pve1", logged.Node);
        Assert.Contains("/dev/sda", logged.Message);
    }

    [Fact]
    public async Task SampleOnceAsync_PurgesSamplesAndErrorsOlderThanRetention()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sampling:RetentionDays"] = "1" })
            .Build();

        var snapshot = new DashboardSnapshot([], [], [], [], DateTime.UtcNow);
        var (sampler, db, _) = Build(snapshot, config);

        db.ResourceSamples.Add(new ResourceSample
        {
            TargetType = "node",
            Node = "pve1",
            CapturedAt = DateTime.UtcNow.AddDays(-5),
        });
        db.DetectedErrorLogs.Add(new DetectedErrorLog
        {
            Source = "api",
            Node = "pve1",
            Message = "old",
            DetectedAt = DateTime.UtcNow.AddDays(-5),
        });
        await db.SaveChangesAsync();

        await sampler.SampleOnceAsync();

        Assert.Empty(await db.ResourceSamples.Where(s => s.CapturedAt < DateTime.UtcNow.AddDays(-1)).ToListAsync());
        Assert.Empty(await db.DetectedErrorLogs.Where(e => e.DetectedAt < DateTime.UtcNow.AddDays(-1)).ToListAsync());
    }

    [Fact]
    public async Task SampleOnceAsync_DoesNotThrow_WhenSnapshotFetchFails()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        var mockDashboard = new Mock<IDashboardService>();
        mockDashboard.Setup(d => d.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));
        services.AddScoped(_ => mockDashboard.Object);
        var provider = services.BuildServiceProvider();

        var sampler = new ResourceSamplingService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ResourceSamplingService>.Instance,
            new ConfigurationBuilder().Build());

        var exception = await Record.ExceptionAsync(() => sampler.SampleOnceAsync());

        Assert.Null(exception);
    }
}
