using HomelabDashboard.Services;
using OtpNet;
using Xunit;

namespace HomelabDashboard.Tests;

public class TotpServiceTests
{
    private readonly TotpService _totp = new();

    [Fact]
    public void GenerateSetup_ProducesValidBase32Secret()
    {
        var setup = _totp.GenerateSetup("pierre");

        var act = () => Base32Encoding.ToBytes(setup.SecretBase32);
        var exception = Record.Exception(act);

        Assert.Null(exception);
    }

    [Fact]
    public void GenerateSetup_OtpAuthUri_ContainsAccountAndSecret()
    {
        var setup = _totp.GenerateSetup("pierre", issuer: "TestApp");

        Assert.Contains("pierre", setup.OtpAuthUri);
        Assert.Contains(setup.SecretBase32, setup.OtpAuthUri);
        Assert.StartsWith("otpauth://totp/", setup.OtpAuthUri);
    }

    [Fact]
    public void GenerateSetup_ProducesNonEmptyQrCode()
    {
        var setup = _totp.GenerateSetup("pierre");

        Assert.NotEmpty(setup.QrCodePngBase64);
    }

    [Fact]
    public void VerifyCode_WithCurrentValidCode_Succeeds()
    {
        var setup = _totp.GenerateSetup("pierre");
        var secretBytes = Base32Encoding.ToBytes(setup.SecretBase32);
        var currentCode = new Totp(secretBytes).ComputeTotp();

        Assert.True(_totp.VerifyCode(setup.SecretBase32, currentCode));
    }

    [Fact]
    public void VerifyCode_WithWrongCode_Fails()
    {
        var setup = _totp.GenerateSetup("pierre");

        Assert.False(_totp.VerifyCode(setup.SecretBase32, "000000"));
    }

    [Fact]
    public void VerifyCode_WithEmptyCode_Fails()
    {
        var setup = _totp.GenerateSetup("pierre");

        Assert.False(_totp.VerifyCode(setup.SecretBase32, ""));
    }

    [Fact]
    public void VerifyCode_CodeFromDifferentSecret_Fails()
    {
        var setupA = _totp.GenerateSetup("pierre");
        var setupB = _totp.GenerateSetup("someone-else");
        var codeForB = new Totp(Base32Encoding.ToBytes(setupB.SecretBase32)).ComputeTotp();

        Assert.False(_totp.VerifyCode(setupA.SecretBase32, codeForB));
    }
}
