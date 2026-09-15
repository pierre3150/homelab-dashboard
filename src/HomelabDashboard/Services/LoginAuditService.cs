using System.Globalization;
using HomelabDashboard.Data;
using HomelabDashboard.Models;

namespace HomelabDashboard.Services;

public interface ILoginAuditService
{
    Task RecordAsync(
        string usernameAttempted,
        int? userId,
        bool success,
        LoginStage stage,
        string ipAddress,
        string userAgentRaw,
        CancellationToken ct = default);
}

/// <summary>
/// Every login attempt - success or failure, password or TOTP stage, real user or
/// bot - is written twice: as a LoginAttempt row (for the in-app audit view) and as
/// one line in a dedicated log file that fail2ban tails. The two lines fail2ban's
/// filter actually looks for are FAILED_LOGIN and BOT_BLOCKED (see
/// fail2ban/filter.d/homelab-dashboard.conf); LOGIN_OK is logged too, purely for
/// visibility ("qui se connecte et d'ou"), fail2ban ignores it.
/// </summary>
public class LoginAuditService : ILoginAuditService
{
    private static readonly object FileLock = new();
    private readonly AppDbContext _db;
    private readonly IDeviceInfoParser _deviceParser;
    private readonly ILogger<LoginAuditService> _logger;
    private readonly string _logFilePath;

    public LoginAuditService(
        AppDbContext db,
        IDeviceInfoParser deviceParser,
        ILogger<LoginAuditService> logger,
        IConfiguration configuration)
    {
        _db = db;
        _deviceParser = deviceParser;
        _logger = logger;
        _logFilePath = configuration["Auth:LogFilePath"] ?? "/data/logs/auth.log";
    }

    public async Task RecordAsync(
        string usernameAttempted,
        int? userId,
        bool success,
        LoginStage stage,
        string ipAddress,
        string userAgentRaw,
        CancellationToken ct = default)
    {
        var device = _deviceParser.Parse(userAgentRaw);

        var attempt = new LoginAttempt
        {
            UsernameAttempted = usernameAttempted,
            UserId = userId,
            Success = success,
            Stage = stage,
            IpAddress = ipAddress,
            UserAgentRaw = userAgentRaw,
            DeviceOs = device.Os,
            DeviceBrowser = device.Browser,
            DeviceFamily = device.DeviceFamily,
        };

        _db.LoginAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);

        AppendToFailoverFriendlyLog(attempt);
    }

    private void AppendToFailoverFriendlyLog(LoginAttempt attempt)
    {
        var tag = attempt.Stage == LoginStage.Bot
            ? "BOT_BLOCKED"
            : attempt.Success ? "LOGIN_OK" : "FAILED_LOGIN";

        var line =
            $"{tag} ip={attempt.IpAddress} user={attempt.UsernameAttempted} " +
            $"stage={attempt.Stage} os=\"{attempt.DeviceOs}\" browser=\"{attempt.DeviceBrowser}\" " +
            $"time={attempt.CreatedAt.ToString("O", CultureInfo.InvariantCulture)}";

        try
        {
            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            lock (FileLock)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            // Never let a logging failure break the login flow itself - but do
            // surface it loudly, since a silently-broken auth log means fail2ban
            // stops protecting the login endpoint without anyone noticing.
            _logger.LogError(ex, "Failed to write auth log line to {Path}", _logFilePath);
        }
    }
}
