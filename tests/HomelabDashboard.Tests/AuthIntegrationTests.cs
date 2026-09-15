using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomelabDashboard.Data;
using HomelabDashboard.Models;
using HomelabDashboard.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HomelabDashboard.Tests;

public class AuthIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthIntegrationTests(WebApplicationFactory<Program> factory)
    {
        // Same lesson learned on every other project here: the in-memory DB name
        // must be created ONCE outside the options delegate, or every request
        // (a new scope each time) would silently get its own empty database.
        var dbName = $"AuthIntegrationTests-{Guid.NewGuid()}";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:LogFilePath"] = Path.Combine(Path.GetTempPath(), $"auth-test-{dbName}.log"),
                    ["Dashboard:KeyRingPath"] = Path.Combine(Path.GetTempPath(), $"keys-{dbName}"),
                });
            });

            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor is not null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(dbName));
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task DashboardSnapshot_WithoutSession_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/dashboard/snapshot");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutSession_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_WithoutSession_IsReachable()
    {
        // The one deliberate exception to "every page requires login": container
        // orchestrators need an unauthenticated health probe.
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongCredentials_ReturnsUnauthorized_AndNoCookieIsSet()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "nobody",
            password = "wrong",
            website = "",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Login_WithHoneypotFilled_IsRejected_EvenWithCorrectPassword()
    {
        await SeedUserAsync("pierre", "correct-password");

        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "pierre",
            password = "correct-password",
            website = "http://bot-filled-this-in.example", // honeypot
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ThenMe_ReturnsOnlyTheAuthenticatedUsersOwnUsername()
    {
        await SeedUserAsync("pierre", "correct-password");

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "pierre",
            password = "correct-password",
            website = "",
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        // /api/auth/me accepts no user identifier from the client at all - proving
        // there is no URL/query parameter that could be swapped to read someone
        // else's data. The endpoint literally cannot be asked for anyone but
        // "whoever this cookie belongs to".
        var meResponse = await _client.GetAsync("/api/auth/me?username=someone-else&userId=999");
        var me = await meResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.Equal("pierre", me.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Login_WithTotpEnabled_DoesNotAuthenticate_UntilTotpStepCompletes()
    {
        var user = await SeedUserAsync("pierre-2fa", "correct-password");
        await EnableTotpForUserAsync(user.Id);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            username = "pierre-2fa",
            password = "correct-password",
            website = "",
        });
        var body = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("totp_required", body.GetProperty("status").GetString());

        // Password alone must NOT have granted a session yet.
        var meResponse = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    private async Task<User> SeedUserAsync(string username, string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();

        var user = new User { Username = username, PasswordHash = hasher.Hash(password) };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task EnableTotpForUserAsync(int userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();

        var user = await db.Users.FindAsync(userId);
        user!.TotpSecretEncrypted = protector.Protect("JBSWY3DPEHPK3PXP");
        user.TotpEnabled = true;
        await db.SaveChangesAsync();
    }
}
