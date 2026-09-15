using HomelabDashboard.Data;
using HomelabDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HomelabDashboard.Services;

public enum PasswordLoginOutcome { Success, RequiresTotp, InvalidCredentials, BotDetected, CaptchaFailed }
public enum TotpLoginOutcome { Success, InvalidCode, ExpiredOrInvalidSession }

public record PasswordLoginResult(PasswordLoginOutcome Outcome, User? User, string? PendingTotpToken);
public record TotpLoginResult(TotpLoginOutcome Outcome, User? User);

public interface IAuthService
{
    Task<PasswordLoginResult> LoginWithPasswordAsync(
        string username, string password, string honeypotField, string? captchaToken,
        string ipAddress, string userAgent, CancellationToken ct = default);

    Task<TotpLoginResult> LoginWithTotpAsync(
        string pendingToken, string code, string ipAddress, string userAgent, CancellationToken ct = default);

    Task<TotpSetup> BeginTotpSetupAsync(int userId, CancellationToken ct = default);
    Task<bool> ConfirmTotpSetupAsync(int userId, string code, CancellationToken ct = default);
}

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly ITotpService _totpService;
    private readonly ISecretProtector _secretProtector;
    private readonly IPendingTotpSessionStore _pendingStore;
    private readonly ICaptchaService _captcha;
    private readonly ILoginAuditService _audit;

    public AuthService(
        AppDbContext db,
        IPasswordHasherService passwordHasher,
        ITotpService totpService,
        ISecretProtector secretProtector,
        IPendingTotpSessionStore pendingStore,
        ICaptchaService captcha,
        ILoginAuditService audit)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _totpService = totpService;
        _secretProtector = secretProtector;
        _pendingStore = pendingStore;
        _captcha = captcha;
        _audit = audit;
    }

    public async Task<PasswordLoginResult> LoginWithPasswordAsync(
        string username, string password, string honeypotField, string? captchaToken,
        string ipAddress, string userAgent, CancellationToken ct = default)
    {
        // Honeypot: a hidden field real browsers never fill. Any value here means
        // a bot blindly submitted every field in the form. Reject before touching
        // the DB or hashing anything, to keep the cost of bot traffic near zero.
        if (!string.IsNullOrEmpty(honeypotField))
        {
            await _audit.RecordAsync(username, null, false, LoginStage.Bot, ipAddress, userAgent, ct);
            return new PasswordLoginResult(PasswordLoginOutcome.BotDetected, null, null);
        }

        if (_captcha.IsEnabled && !await _captcha.VerifyAsync(captchaToken, ct))
        {
            await _audit.RecordAsync(username, null, false, LoginStage.Bot, ipAddress, userAgent, ct);
            return new PasswordLoginResult(PasswordLoginOutcome.CaptchaFailed, null, null);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

        // Constant-shape failure path: whether the username exists or the password
        // is wrong, we return the same outcome and take a comparable amount of
        // work, so a timing/response difference can't be used to enumerate valid
        // usernames.
        if (user is null || !_passwordHasher.Verify(user, password))
        {
            await _audit.RecordAsync(username, user?.Id, false, LoginStage.Password, ipAddress, userAgent, ct);
            return new PasswordLoginResult(PasswordLoginOutcome.InvalidCredentials, null, null);
        }

        if (user.TotpEnabled)
        {
            await _audit.RecordAsync(username, user.Id, true, LoginStage.Password, ipAddress, userAgent, ct);
            var pendingToken = _pendingStore.Create(user.Id);
            return new PasswordLoginResult(PasswordLoginOutcome.RequiresTotp, user, pendingToken);
        }

        await _audit.RecordAsync(username, user.Id, true, LoginStage.Password, ipAddress, userAgent, ct);
        return new PasswordLoginResult(PasswordLoginOutcome.Success, user, null);
    }

    public async Task<TotpLoginResult> LoginWithTotpAsync(
        string pendingToken, string code, string ipAddress, string userAgent, CancellationToken ct = default)
    {
        var userId = _pendingStore.Consume(pendingToken);
        if (userId is null)
            return new TotpLoginResult(TotpLoginOutcome.ExpiredOrInvalidSession, null);

        var user = await _db.Users.FindAsync([userId.Value], ct);
        if (user is null || !user.TotpEnabled || user.TotpSecretEncrypted is null)
            return new TotpLoginResult(TotpLoginOutcome.ExpiredOrInvalidSession, null);

        var secret = _secretProtector.Unprotect(user.TotpSecretEncrypted);
        var isValid = _totpService.VerifyCode(secret, code);

        await _audit.RecordAsync(user.Username, user.Id, isValid, LoginStage.Totp, ipAddress, userAgent, ct);

        return isValid
            ? new TotpLoginResult(TotpLoginOutcome.Success, user)
            : new TotpLoginResult(TotpLoginOutcome.InvalidCode, null);
    }

    public async Task<TotpSetup> BeginTotpSetupAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct)
            ?? throw new InvalidOperationException("User not found.");

        var setup = _totpService.GenerateSetup(user.Username);
        user.TotpSecretEncrypted = _secretProtector.Protect(setup.SecretBase32);
        user.TotpEnabled = false; // not enabled until confirmed
        await _db.SaveChangesAsync(ct);

        return setup;
    }

    public async Task<bool> ConfirmTotpSetupAsync(int userId, string code, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user?.TotpSecretEncrypted is null) return false;

        var secret = _secretProtector.Unprotect(user.TotpSecretEncrypted);
        if (!_totpService.VerifyCode(secret, code)) return false;

        user.TotpEnabled = true;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
