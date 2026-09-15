namespace HomelabDashboard.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>TOTP secret (Base32), encrypted at rest. Null until 2FA setup begins.</summary>
    public string? TotpSecretEncrypted { get; set; }

    /// <summary>True only once the user has confirmed a code against TotpSecretEncrypted.
    /// A secret can exist (mid-setup) without this being true yet.</summary>
    public bool TotpEnabled { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum LoginStage
{
    Password,   // first factor
    Totp,       // second factor
    Bot         // rejected before reaching real auth logic (honeypot / captcha failure)
}

/// <summary>
/// One row per authentication attempt, success or failure, at every stage.
/// This is the audit trail: who tried to log in, from where, with what device -
/// and it's also the source fail2ban tails (via the dedicated log file the
/// same data is mirrored to - see LoginAuditService).
/// </summary>
public class LoginAttempt
{
    public int Id { get; set; }
    public string UsernameAttempted { get; set; } = string.Empty;
    public int? UserId { get; set; }
    public bool Success { get; set; }
    public LoginStage Stage { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgentRaw { get; set; } = string.Empty;
    public string? DeviceOs { get; set; }
    public string? DeviceBrowser { get; set; }
    public string? DeviceFamily { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
