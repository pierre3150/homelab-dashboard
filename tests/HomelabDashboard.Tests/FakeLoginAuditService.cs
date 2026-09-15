using HomelabDashboard.Models;
using HomelabDashboard.Services;

namespace HomelabDashboard.Tests;

public record RecordedAttempt(string Username, int? UserId, bool Success, LoginStage Stage, string Ip, string UserAgent);

/// <summary>In-memory stand-in for ILoginAuditService - avoids touching the
/// filesystem in unit tests while still letting tests assert what would have
/// been logged.</summary>
public class FakeLoginAuditService : ILoginAuditService
{
    public List<RecordedAttempt> Recorded { get; } = new();

    public Task RecordAsync(
        string usernameAttempted, int? userId, bool success, LoginStage stage,
        string ipAddress, string userAgentRaw, CancellationToken ct = default)
    {
        Recorded.Add(new RecordedAttempt(usernameAttempted, userId, success, stage, ipAddress, userAgentRaw));
        return Task.CompletedTask;
    }
}
