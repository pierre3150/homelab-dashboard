using System.Threading.RateLimiting;
using HomelabDashboard.Data;
using HomelabDashboard.Models;
using HomelabDashboard.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Database ---
var dbPath = builder.Configuration["Dashboard:DbPath"] ?? "/data/dashboard.db";
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

// --- Data Protection (encrypts TOTP secrets at rest, and signs/encrypts the
// session cookie itself) ---
// Keys are persisted to disk so they survive container restarts/redeploys - if
// they didn't, every TOTP secret would become unreadable (and every user locked
// out of 2FA), and every session cookie would be invalidated, on every redeploy.
var keyRingPath = builder.Configuration["Dashboard:KeyRingPath"] ?? "/data/keys";
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

// --- Proxmox client (unchanged) ---
builder.Services.AddHttpClient<IProxmoxClient, ProxmoxClient>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
builder.Services.AddScoped<IDashboardService, DashboardService>();

// --- Auth services ---
builder.Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddSingleton<ITotpService, TotpService>();
builder.Services.AddSingleton<ISecretProtector, SecretProtector>();
builder.Services.AddSingleton<IPendingTotpSessionStore, PendingTotpSessionStore>();
builder.Services.AddSingleton<IDeviceInfoParser, DeviceInfoParser>();
builder.Services.AddScoped<ILoginAuditService, LoginAuditService>();
builder.Services.AddHttpClient<ICaptchaService, HCaptchaService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// --- Cookie authentication ---
// Every page/endpoint requires a valid session by default (see the global
// AuthorizeFilter below) - this cookie IS that verification, checked on every
// single request, not just once at the door.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "hld_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // requires HTTPS (nginx terminates TLS)
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        // This is an API consumed by a JS frontend, not a server-rendered login
        // page - on an unauthenticated request, return 401 JSON instead of a
        // redirect the frontend would have to special-case.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

// --- Global authorization: every controller requires auth unless explicitly
// marked [AllowAnonymous] (login endpoints, /health). This is the "toutes les
// pages verifient la connexion" requirement, enforced in one place rather than
// hoping every new endpoint remembers to add [Authorize] itself. ---
builder.Services.AddControllers(options =>
{
    options.Filters.Add(new AuthorizeFilter());
});

// --- Rate limiting on the login endpoints - defense in depth alongside
// fail2ban. Fail2ban bans the IP entirely after 3 failures (10 min); this caps
// the burst *before* fail2ban's findtime window even closes, and still applies
// even if fail2ban isn't running (e.g. during local testing). ---
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 8;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Trop de tentatives, reessaie dans un instant.", ct);
    };
});

var app = builder.Build();

// --- Trust X-Forwarded-For/-Proto from nginx so RemoteIpAddress and the
// cookie's "Secure" checks see the real client, not the reverse proxy. Restrict
// to nginx's own network hop only - see docker-compose.yml / nginx conf. ---
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SeedAdminUserIfNoneExistsAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasherService>());
}

app.Run();

static async Task SeedAdminUserIfNoneExistsAsync(AppDbContext db, IPasswordHasherService hasher)
{
    if (await db.Users.AnyAsync()) return;

    var username = Environment.GetEnvironmentVariable("ADMIN_USERNAME");
    var password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        // No admin seeded and no env vars provided: the app still starts (so
        // /health works for orchestrators), but nobody can log in until this
        // is fixed - which is the safe failure mode for an auth system.
        return;
    }

    var user = new User { Username = username };
    user.PasswordHash = hasher.Hash(password);
    db.Users.Add(user);
    await db.SaveChangesAsync();
}

public partial class Program { }
