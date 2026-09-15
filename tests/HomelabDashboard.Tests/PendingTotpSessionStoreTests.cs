using HomelabDashboard.Services;
using Xunit;

namespace HomelabDashboard.Tests;

public class PendingTotpSessionStoreTests
{
    private readonly PendingTotpSessionStore _store = new();

    [Fact]
    public void Create_ThenConsume_ReturnsUserId()
    {
        var token = _store.Create(42);

        var userId = _store.Consume(token);

        Assert.Equal(42, userId);
    }

    [Fact]
    public void Consume_IsSingleUse()
    {
        var token = _store.Create(42);
        _store.Consume(token);

        var secondAttempt = _store.Consume(token);

        Assert.Null(secondAttempt);
    }

    [Fact]
    public void Consume_UnknownToken_ReturnsNull()
    {
        var result = _store.Consume("this-token-was-never-created");

        Assert.Null(result);
    }

    [Fact]
    public void Create_ProducesUniqueTokensForDifferentSessions()
    {
        var token1 = _store.Create(1);
        var token2 = _store.Create(2);

        Assert.NotEqual(token1, token2);
    }
}
