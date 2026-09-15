using HomelabDashboard.Data;
using HomelabDashboard.Models;
using HomelabDashboard.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OtpNet;
using Xunit;

namespace HomelabDashboard.Tests;

public class AuthServiceTests
{
    private readonly FakeLoginAuditService _audit = new();

    private (AuthService service, AppDbContext db, ITotpService totp, ISecretProtector protector) CreateService()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(dbOptions);

        var dpServices = new ServiceCollection();
        dpServices.AddDataProtection();
        var dpProvider = dpServices.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
        var protector = new SecretProtector(dpProvider);

        var hasher = new PasswordHasherService();
        var totp = new TotpService();
        var pendingStore = new PendingTotpSessionStore();

        var captchaMock = new Mock<ICaptchaService>();
        captchaMock.Setup(c => c.IsEnabled).Returns(false); // disabled by default in tests

        var service = new AuthService(db, hasher, totp, protector, pendingStore, captchaMock.Object, _audit);
        return (service, db, totp, protector);
    }

    private static async Task<User> SeedUserAsync(AppDbContext db, string username, string password)
    {
        var hasher = new PasswordHasherService();
        var user = new User { Username = username, PasswordHash = hasher.Hash(password) };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task LoginWithPasswordAsync_CorrectCredentials_NoTotp_ReturnsSuccess()
    {
        var (service, db, _, _) = CreateService();
        await SeedUserAsync(db, "pierre", "correct-password");

        var result = await service.LoginWithPasswordAsync(
            "pierre", "correct-password", honeypotField: "", captchaToken: null,
            ipAddress: "1.2.3.4", userAgent: "test-agent");

        Assert.Equal(PasswordLoginOutcome.Success, result.Outcome);
        Assert.Equal("pierre", result.User!.Username);
    }

    [Fact]
    public async Task LoginWithPasswordAsync_WrongPassword_ReturnsInvalidCredentials()
    {
        var (service, db, _, _) = CreateService();
        await SeedUserAsync(db, "pierre", "correct-password");

        var result = await service.LoginWithPasswordAsync(
            "pierre", "wrong-password", "", null, "1.2.3.4", "test-agent");

        Assert.Equal(PasswordLoginOutcome.InvalidCredentials, result.Outcome);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task LoginWithPasswordAsync_UnknownUsername_ReturnsInvalidCredentials()
    {
        var (service, _, _, _) = CreateService();

        var result = await service.LoginWithPasswordAsync(
            "ghost", "whatever", "", null, "1.2.3.4", "test-agent");

        Assert.Equal(PasswordLoginOutcome.InvalidCredentials, result.Outcome);
    }

    [Fact]
    public async Task LoginWithPasswordAsync_HoneypotFilled_ReturnsBotDetected_AndSkipsPasswordCheck()
    {
        var (service, db, _, _) = CreateService();
        await SeedUserAsync(db, "pierre", "correct-password");

        var result = await service.LoginWithPasswordAsync(
            "pierre", "correct-password", honeypotField: "http://spam.example",
            captchaToken: null, ipAddress: "9.9.9.9", userAgent: "bot-agent");

        Assert.Equal(PasswordLoginOutcome.BotDetected, result.Outcome);
        Assert.Single(_audit.Recorded);
        Assert.Equal(LoginStage.Bot, _audit.Recorded[0].Stage);
    }

    [Fact]
    public async Task LoginWithPasswordAsync_EveryAttempt_IsAudited()
    {
        var (service, db, _, _) = CreateService();
        await SeedUserAsync(db, "pierre", "correct-password");

        await service.LoginWithPasswordAsync("pierre", "wrong", "", null, "1.2.3.4", "ua-1");
        await service.LoginWithPasswordAsync("pierre", "correct-password", "", null, "5.6.7.8", "ua-2");

        Assert.Equal(2, _audit.Recorded.Count);
        Assert.False(_audit.Recorded[0].Success);
        Assert.Equal("1.2.3.4", _audit.Recorded[0].Ip);
        Assert.True(_audit.Recorded[1].Success);
        Assert.Equal("5.6.7.8", _audit.Recorded[1].Ip);
    }

    [Fact]
    public async Task LoginWithPasswordAsync_UserWithTotpEnabled_RequiresTotp_DoesNotFullyAuthenticate()
    {
        var (service, db, totp, protector) = CreateService();
        var user = await SeedUserAsync(db, "pierre", "correct-password");
        user.TotpSecretEncrypted = protector.Protect(Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20)));
        user.TotpEnabled = true;
        await db.SaveChangesAsync();

        var result = await service.LoginWithPasswordAsync(
            "pierre", "correct-password", "", null, "1.2.3.4", "test-agent");

        Assert.Equal(PasswordLoginOutcome.RequiresTotp, result.Outcome);
        Assert.NotNull(result.PendingTotpToken);
    }

    [Fact]
    public async Task LoginWithTotpAsync_CorrectCode_Succeeds()
    {
        var (service, db, totp, protector) = CreateService();
        var user = await SeedUserAsync(db, "pierre", "correct-password");
        var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        user.TotpSecretEncrypted = protector.Protect(secret);
        user.TotpEnabled = true;
        await db.SaveChangesAsync();

        var passwordResult = await service.LoginWithPasswordAsync(
            "pierre", "correct-password", "", null, "1.2.3.4", "test-agent");
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

        var totpResult = await service.LoginWithTotpAsync(
            passwordResult.PendingTotpToken!, code, "1.2.3.4", "test-agent");

        Assert.Equal(TotpLoginOutcome.Success, totpResult.Outcome);
        Assert.Equal("pierre", totpResult.User!.Username);
    }

    [Fact]
    public async Task LoginWithTotpAsync_WrongCode_Fails()
    {
        var (service, db, totp, protector) = CreateService();
        var user = await SeedUserAsync(db, "pierre", "correct-password");
        user.TotpSecretEncrypted = protector.Protect(Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20)));
        user.TotpEnabled = true;
        await db.SaveChangesAsync();

        var passwordResult = await service.LoginWithPasswordAsync(
            "pierre", "correct-password", "", null, "1.2.3.4", "test-agent");

        var totpResult = await service.LoginWithTotpAsync(
            passwordResult.PendingTotpToken!, "000000", "1.2.3.4", "test-agent");

        Assert.Equal(TotpLoginOutcome.InvalidCode, totpResult.Outcome);
    }

    [Fact]
    public async Task LoginWithTotpAsync_ReusedPendingToken_FailsSecondTime()
    {
        var (service, db, totp, protector) = CreateService();
        var user = await SeedUserAsync(db, "pierre", "correct-password");
        var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        user.TotpSecretEncrypted = protector.Protect(secret);
        user.TotpEnabled = true;
        await db.SaveChangesAsync();

        var passwordResult = await service.LoginWithPasswordAsync(
            "pierre", "correct-password", "", null, "1.2.3.4", "test-agent");
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();

        await service.LoginWithTotpAsync(passwordResult.PendingTotpToken!, code, "1.2.3.4", "test-agent");
        var secondAttempt = await service.LoginWithTotpAsync(
            passwordResult.PendingTotpToken!, code, "1.2.3.4", "test-agent");

        Assert.Equal(TotpLoginOutcome.ExpiredOrInvalidSession, secondAttempt.Outcome);
    }

    [Fact]
    public async Task BeginTotpSetupAsync_ThenConfirm_EnablesTotp()
    {
        var (service, db, _, _) = CreateService();
        var user = await SeedUserAsync(db, "pierre", "correct-password");

        var setup = await service.BeginTotpSetupAsync(user.Id);
        var code = new Totp(Base32Encoding.ToBytes(setup.SecretBase32)).ComputeTotp();
        var confirmed = await service.ConfirmTotpSetupAsync(user.Id, code);

        var reloaded = await db.Users.FindAsync(user.Id);
        Assert.True(confirmed);
        Assert.True(reloaded!.TotpEnabled);
    }

    [Fact]
    public async Task ConfirmTotpSetupAsync_WrongCode_DoesNotEnableTotp()
    {
        var (service, db, _, _) = CreateService();
        var user = await SeedUserAsync(db, "pierre", "correct-password");
        await service.BeginTotpSetupAsync(user.Id);

        var confirmed = await service.ConfirmTotpSetupAsync(user.Id, "000000");

        var reloaded = await db.Users.FindAsync(user.Id);
        Assert.False(confirmed);
        Assert.False(reloaded!.TotpEnabled);
    }
}
