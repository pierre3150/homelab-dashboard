namespace HomelabDashboard.Middleware;

/// <summary>
/// Lightweight API-key gate. Health check is always open (for container/orchestrator
/// probes); every other route needs the X-Api-Key header to match DASHBOARD_API_KEY.
/// Deliberately simple: this sits on a private LAN behind the homelab's own network
/// boundary, so a shared-secret header is proportionate - not a public-internet-facing
/// OAuth flow.
/// </summary>
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private const string HeaderName = "X-Api-Key";

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IConfiguration configuration)
    {
        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/index.html") ||
            context.Request.Path == "/" ||
            !context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var expectedKey = configuration["Dashboard:ApiKey"];

        if (string.IsNullOrEmpty(expectedKey))
        {
            // No key configured: fail closed rather than silently open, so a missing
            // env var on deploy can't accidentally expose the API unauthenticated.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Dashboard API key is not configured on the server.");
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var providedKey) ||
            providedKey != expectedKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing or invalid X-Api-Key header.");
            return;
        }

        await _next(context);
    }
}
