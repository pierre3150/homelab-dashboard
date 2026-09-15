using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace HomelabDashboard.Services;

/// <summary>
/// Tracks the short window between "password correct" and "TOTP code confirmed".
/// The pending token proves the password stage passed, without yet granting a
/// real session cookie - nothing is authenticated until the second factor is
/// also verified. Tokens expire quickly (5 min) and are single-use.
/// </summary>
public interface IPendingTotpSessionStore
{
    string Create(int userId);
    int? Consume(string token);
}

public class PendingTotpSessionStore : IPendingTotpSessionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, (int UserId, DateTime ExpiresAt)> _store = new();

    public string Create(int userId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _store[token] = (userId, DateTime.UtcNow.Add(Ttl));
        return token;
    }

    public int? Consume(string token)
    {
        if (!_store.TryRemove(token, out var entry)) return null;
        return entry.ExpiresAt >= DateTime.UtcNow ? entry.UserId : null;
    }
}
