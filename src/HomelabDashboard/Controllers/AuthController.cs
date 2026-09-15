using System.Security.Claims;
using HomelabDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HomelabDashboard.Controllers;

public record LoginRequest(string Username, string Password, string? Website, string? CaptchaToken);
public record TotpVerifyRequest(string PendingToken, string Code);
public record TotpConfirmRequest(string Code);

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("login")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var ip = GetClientIp();
        var userAgent = Request.Headers.UserAgent.ToString();

        // "Website" is the honeypot field name (looks plausible to a bot, a real
        // user never sees or fills it - it's hidden via CSS in the login page).
        var result = await _authService.LoginWithPasswordAsync(
            request.Username, request.Password, request.Website ?? string.Empty,
            request.CaptchaToken, ip, userAgent, ct);

        switch (result.Outcome)
        {
            case PasswordLoginOutcome.Success:
                await SignInAsync(result.User!.Id, result.User.Username);
                return Ok(new { status = "authenticated" });

            case PasswordLoginOutcome.RequiresTotp:
                return Ok(new { status = "totp_required", pendingToken = result.PendingTotpToken });

            case PasswordLoginOutcome.BotDetected:
            case PasswordLoginOutcome.CaptchaFailed:
                // Deliberately identical response to InvalidCredentials: don't tell
                // an attacker (or their script) which specific defense caught them.
                return Unauthorized(new { error = "Identifiants invalides." });

            default:
                return Unauthorized(new { error = "Identifiants invalides." });
        }
    }

    [HttpPost("login/totp")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginTotp([FromBody] TotpVerifyRequest request, CancellationToken ct)
    {
        var ip = GetClientIp();
        var userAgent = Request.Headers.UserAgent.ToString();

        var result = await _authService.LoginWithTotpAsync(request.PendingToken, request.Code, ip, userAgent, ct);

        if (result.Outcome != TotpLoginOutcome.Success)
            return Unauthorized(new { error = "Code invalide ou session expiree." });

        await SignInAsync(result.User!.Id, result.User.Username);
        return Ok(new { status = "authenticated" });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { status = "logged_out" });
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        // Derived strictly from the authenticated cookie's claims - never from
        // anything the client could supply in the URL or body. This is the
        // pattern every "give me MY data" endpoint in this app follows.
        var username = User.FindFirstValue(ClaimTypes.Name);
        var totpEnabled = User.FindFirstValue("totp_enabled") == "true";
        return Ok(new { username, totpEnabled });
    }

    [HttpPost("totp/setup")]
    [Authorize]
    public async Task<IActionResult> SetupTotp(CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        var setup = await _authService.BeginTotpSetupAsync(userId, ct);
        return Ok(new { otpAuthUri = setup.OtpAuthUri, qrCodePngBase64 = setup.QrCodePngBase64 });
    }

    [HttpPost("totp/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmTotp([FromBody] TotpConfirmRequest request, CancellationToken ct)
    {
        var userId = GetAuthenticatedUserId();
        var confirmed = await _authService.ConfirmTotpSetupAsync(userId, request.Code, ct);

        if (!confirmed) return BadRequest(new { error = "Code invalide." });

        // Refresh the session so the totp_enabled claim reflects reality immediately.
        await SignInAsync(userId, User.FindFirstValue(ClaimTypes.Name)!, totpEnabled: true);
        return Ok(new { status = "totp_enabled" });
    }

    private int GetAuthenticatedUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private string GetClientIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private async Task SignInAsync(int userId, string username, bool totpEnabled = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new("totp_enabled", totpEnabled.ToString().ToLowerInvariant()),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }
}
