using HomelabDashboard.Middleware;
using HomelabDashboard.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddHttpClient<IProxmoxClient, ProxmoxClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // Homelab Proxmox instances almost always run with a self-signed cert.
        // Trusting it here is a deliberate, documented trade-off for a private
        // LAN deployment - do not reuse this handler for any public-facing call.
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

builder.Services.AddScoped<IDashboardService, DashboardService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseMiddleware<ApiKeyMiddleware>();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

public partial class Program { }
