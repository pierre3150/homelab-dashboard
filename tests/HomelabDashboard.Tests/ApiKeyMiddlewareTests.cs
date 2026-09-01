using System.Linq;
using System.Net;
using System.Net.Http.Json;
using HomelabDashboard.Models;
using HomelabDashboard.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HomelabDashboard.Tests;

public class ApiKeyMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestApiKey = "unit-test-secret";

    private static readonly DashboardSnapshot FakeSnapshot = new(
        Nodes: [new NodeStatus("pve1", "online", 10, 1_000_000, 2_000_000, 100)],
        Guests: [],
        FetchedAt: DateTime.UtcNow
    );

    private readonly HttpClient _client;

    public ApiKeyMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        var customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dashboard:ApiKey"] = TestApiKey
                });
            });

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IDashboardService));
                if (descriptor is not null) services.Remove(descriptor);

                services.AddScoped<IDashboardService>(_ => new FakeDashboardService(FakeSnapshot));
            });
        });

        _client = customFactory.CreateClient();
    }

    [Fact]
    public async Task Health_DoesNotRequireApiKey()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Snapshot_WithoutApiKey_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/dashboard/snapshot");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Snapshot_WithWrongApiKey_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
        var response = await _client.GetAsync("/api/dashboard/snapshot");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Snapshot_WithCorrectApiKey_ReturnsOk()
    {
        _client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        var response = await _client.GetAsync("/api/dashboard/snapshot");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var snapshot = await response.Content.ReadFromJsonAsync<DashboardSnapshot>();
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Nodes);
    }

    private class FakeDashboardService : IDashboardService
    {
        private readonly DashboardSnapshot _snapshot;
        public FakeDashboardService(DashboardSnapshot snapshot) => _snapshot = snapshot;
        public Task<DashboardSnapshot> GetSnapshotAsync(CancellationToken ct = default)
            => Task.FromResult(_snapshot);
    }
}
