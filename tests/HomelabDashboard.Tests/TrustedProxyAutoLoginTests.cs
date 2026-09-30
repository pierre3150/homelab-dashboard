using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomelabDashboard.Data;
using HomelabDashboard.Middleware;
using HomelabDashboard.Models;
using HomelabDashboard.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HomelabDashboard.Tests;

/// <summary>
/// Exercises TrustedProxyAutoLoginMiddleware through the real HTTP pipeline
/// (not by calling it directly) since its whole point is where it sits
/// relative to UseAuthentication/UseAuthorization - a unit test that new'd it
/// up standalone wouldn't prove that ordering is actually correct.
/// </summary>
public class TrustedProxyAutoLoginTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Secret = "test-shared-secret-do-not-use-in-prod";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public TrustedProxyAutoLoginTests(WebApplicationFactory<Program> factory)
    {
        var dbName = $"TrustedProxyTests-{Guid.NewGuid()}";

        Environment.SetEnvironmentVariable("DASHBOARD_KEYRING_PATH", Path.Combine(Path.GetTempPath(), $"keys-{dbName}"));
        Environment.SetEnvironmentVariable("DASHBOARD_DB_PATH", Path.Combine(Path.GetTempPath(), $"unused-{dbName}.db"));

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:LogFilePath"] = Path.Combine(Path.GetTempPath(), $"auth-test-{dbName}.log"),
                    ["TrustedProxy:SharedSecret"] = Secret,
                });
            });

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor is not null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
            });
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
    }

    [Fact]
    public async Task RequestWithCorrectSecretAndKnownUser_IsAutoAuthenticated_NoLoginNeeded()
    {
        await SeedUserAsync("pierre");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add(TrustedProxyAutoLoginMiddleware.SharedSecretHeaderName, Secret);
        request.Headers.Add(TrustedProxyAutoLoginMiddleware.RemoteUserHeaderName, "pierre");

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("pierre", body.GetProperty("username").GetString());
    }

    [Fact]
    public async Task RequestWithWrongSecret_FallsThroughToNormalAuth_AndIsUnauthorized()
    {
        await SeedUserAsync("pierre");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add(TrustedProxyAutoLoginMiddleware.SharedSecretHeaderName, "totally-wrong-secret");
        request.Headers.Add(TrustedProxyAutoLoginMiddleware.RemoteUserHeaderName, "pierre");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestWithCorrectSecret_ButUnknownUsername_IsNotAutoProvisioned()
    {
        // No SeedUserAsync call - "someone-not-local" doesn't exist in the DB.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add(TrustedProxyAutoLoginMiddleware.SharedSecretHeaderName, Secret);
        request.Headers.Add(TrustedProxyAutoLoginMiddleware.RemoteUserHeaderName, "someone-not-local");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestWithNoTrustedHeadersAtAll_BehavesExactlyLikeBefore_Unauthorized()
    {
        await SeedUserAsync("pierre");

        // Plain request, no trusted-proxy headers - the direct-IP / no-NPM path.
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AutoLoggedInUser_TotpEnabledClaim_ReflectsTheLocalAccountState()
    {
        var user = await SeedUserAsync("pierre-2fa");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            var tracked = await db.Users.FindAsync(user.Id);
            tracked!.TotpSecretEncrypted = protector.Protect("JBSWY3DPEHPK3PXP");
            tracked.TotpEnabled = true;
            await db.SaveChangesAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add(TrustedProxyAutoLoginMiddleware.SharedSecretHeaderName, Secret);
        request.Headers.Add(TrustedProxyAutoLoginMiddleware.RemoteUserHeaderName, "pierre-2fa");

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("totpEnabled").GetBoolean());
    }

    private async Task<User> SeedUserAsync(string username)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();

        var user = new User { Username = username, PasswordHash = hasher.Hash("irrelevant-for-these-tests") };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
