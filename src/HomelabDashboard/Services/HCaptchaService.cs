using System.Text.Json;

namespace HomelabDashboard.Services;

public interface ICaptchaService
{
    bool IsEnabled { get; }
    Task<bool> VerifyAsync(string? token, CancellationToken ct = default);
}

/// <summary>
/// hCaptcha verification - entirely optional. If HCAPTCHA__SECRETKEY isn't
/// configured, IsEnabled is false and the login flow skips captcha checking
/// (falls back to the honeypot field + rate limiting + fail2ban alone).
/// Get free keys at https://www.hcaptcha.com/.
/// </summary>
public class HCaptchaService : ICaptchaService
{
    private const string VerifyUrl = "https://hcaptcha.com/siteverify";
    private readonly HttpClient _http;
    private readonly string? _secretKey;
    private readonly ILogger<HCaptchaService> _logger;

    public HCaptchaService(HttpClient http, IConfiguration configuration, ILogger<HCaptchaService> logger)
    {
        _http = http;
        _secretKey = configuration["Captcha:HCaptchaSecretKey"];
        _logger = logger;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_secretKey);

    public async Task<bool> VerifyAsync(string? token, CancellationToken ct = default)
    {
        if (!IsEnabled) return true; // captcha not configured: don't block login on it
        if (string.IsNullOrWhiteSpace(token)) return false;

        try
        {
            var response = await _http.PostAsync(
                VerifyUrl,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = _secretKey!,
                    ["response"] = token,
                }),
                ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("success", out var successProp) && successProp.GetBoolean();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "hCaptcha verification call failed");
            return false; // fail closed: a broken captcha check should not open the door
        }
    }
}
