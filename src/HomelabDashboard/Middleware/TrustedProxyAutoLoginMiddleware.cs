using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HomelabDashboard.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace HomelabDashboard.Middleware;

/// <summary>
/// Lets a trusted reverse proxy (NPM, forwarding a request already verified by
/// Authelia) sign the user straight in, so a request arriving through the
/// public Authelia-gated URL never has to pass through the app's own
/// password/TOTP form on top of Authelia's - one login, not two.
///
/// This is opt-in and off by default: without TrustedProxy:SharedSecret
/// configured, this middleware is a no-op and every request goes through the
/// normal cookie/password/TOTP flow exactly as before. Once configured:
///
///   - It only ever acts on a request that is NOT already authenticated -
///     an existing valid session cookie is left alone.
///   - It requires BOTH a shared secret header (known only to whoever
///     configures NPM's proxy host - never sent by a browser on its own) AND
///     an Authelia "Remote-User" header, compared in constant time.
///   - It only signs in as a user that already exists locally by username -
///     it never creates accounts, so a spoofed/misconfigured header can, at
///     worst, name a real local username and still needs the correct secret.
///   - Any request missing the secret, or presenting the wrong one, or naming
///     an unknown user, falls straight through to the app's own login screen -
///     that's the safety net for direct/local access that doesn't go through
///     NPM+Authelia at all.
///
/// The shared secret is what actually does the trust-establishing here: the
/// app has no way to verify "this request really came from NPM" other than
/// that secret, since NPM and the app are separate containers talking over
/// the LAN. Keep it long, random, and only ever set in NPM's proxy host
/// config and this app's environment - never anywhere a browser could see it.
/// </summary>
public class TrustedProxyAutoLoginMiddleware
{
    public const string SharedSecretHeaderName = "X-Trusted-Proxy-Secret";
    public const string RemoteUserHeaderName = "Remote-User";

    private readonly RequestDelegate _next;
    private readonly ILogger<TrustedProxyAutoLoginMiddleware> _logger;

    public TrustedProxyAutoLoginMiddleware(RequestDelegate next, ILogger<TrustedProxyAutoLoginMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration, AppDbContext db)
    {
        var configuredSecret = configuration["TrustedProxy:SharedSecret"];

        if (!string.IsNullOrEmpty(configuredSecret) && context.User.Identity?.IsAuthenticated != true)
        {
            await TryAutoLoginAsync(context, db, configuredSecret);
        }

        await _next(context);
    }

    private async Task TryAutoLoginAsync(HttpContext context, AppDbContext db, string configuredSecret)
    {
        var providedSecret = context.Request.Headers[SharedSecretHeaderName].ToString();
        var remoteUser = context.Request.Headers[RemoteUserHeaderName].ToString();

        if (string.IsNullOrEmpty(providedSecret))
        {
            return; // Not coming through the trusted proxy path at all - normal login flow.
        }

        if (!FixedTimeEquals(providedSecret, configuredSecret))
        {
            _logger.LogWarning(
                "Trusted proxy header presented with a wrong secret from {RemoteIp}",
                context.Connection.RemoteIpAddress);
            return;
        }

        if (string.IsNullOrEmpty(remoteUser))
        {
            _logger.LogWarning("Trusted proxy secret was valid but no Remote-User header was present");
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == remoteUser.ToLower());
        if (user is null)
        {
            _logger.LogWarning(
                "Trusted proxy header named unknown local user {RemoteUser} - refusing to auto-provision", remoteUser);
            return;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("totp_enabled", user.TotpEnabled.ToString().ToLowerInvariant()),
            new("auth_method", "authelia"),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        // Set it on the current request immediately, so the authorization check
        // further down the pipeline sees it without waiting for a round trip...
        context.User = principal;

        // ...and also actually sign in, so subsequent requests in the same
        // browser session carry a normal session cookie instead of needing
        // this header re-verified on every single call.
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
    }

    /// <summary>
    /// Constant-time string comparison so a wrong guess doesn't leak, via
    /// response timing, how many leading characters it got right.
    /// </summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);

        if (aBytes.Length != bBytes.Length)
        {
            // Still run a fixed-time comparison of equal-length buffers so a
            // length mismatch doesn't return measurably faster than a
            // same-length near-miss.
            CryptographicOperations.FixedTimeEquals(aBytes, aBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
