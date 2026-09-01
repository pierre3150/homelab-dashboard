using HomelabDashboard.Models;
using HomelabDashboard.Services;
using Moq;
using Xunit;

namespace HomelabDashboard.Tests;

public class DashboardServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_AggregatesGuestsAcrossAllNodes()
    {
        var mockClient = new Mock<IProxmoxClient>();

        mockClient.Setup(c => c.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NodeStatus>
            {
                new("pve1", "online", 12.5, 4_000_000_000, 16_000_000_000, 86400),
                new("pve2", "online", 8.0, 2_000_000_000, 8_000_000_000, 43200),
            });

        mockClient.Setup(c => c.GetGuestsAsync("pve1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GuestSummary>
            {
                new(100, "jellyfin", "lxc", "running", 5.0, 500_000_000, 2_000_000_000, 3600),
            });

        mockClient.Setup(c => c.GetGuestsAsync("pve2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GuestSummary>
            {
                new(200, "nextcloud", "lxc", "running", 3.0, 300_000_000, 1_000_000_000, 7200),
                new(201, "windows-vm", "qemu", "stopped", 0, 0, 4_000_000_000, 0),
            });

        var service = new DashboardService(mockClient.Object);

        var snapshot = await service.GetSnapshotAsync();

        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Equal(3, snapshot.Guests.Count);
        Assert.Contains(snapshot.Guests, g => g.Name == "jellyfin");
        Assert.Contains(snapshot.Guests, g => g.Name == "nextcloud");
        Assert.Contains(snapshot.Guests, g => g.Name == "windows-vm" && g.Status == "stopped");
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsEmptyGuests_WhenNoNodes()
    {
        var mockClient = new Mock<IProxmoxClient>();
        mockClient.Setup(c => c.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NodeStatus>());

        var service = new DashboardService(mockClient.Object);

        var snapshot = await service.GetSnapshotAsync();

        Assert.Empty(snapshot.Nodes);
        Assert.Empty(snapshot.Guests);
        mockClient.Verify(c => c.GetGuestsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSnapshotAsync_SetsFetchedAtToRecentUtcTime()
    {
        var mockClient = new Mock<IProxmoxClient>();
        mockClient.Setup(c => c.GetNodesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<NodeStatus>());

        var service = new DashboardService(mockClient.Object);
        var before = DateTime.UtcNow;

        var snapshot = await service.GetSnapshotAsync();

        Assert.InRange(snapshot.FetchedAt, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }
}
