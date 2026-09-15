using OtpNet;
using QRCoder;

namespace HomelabDashboard.Services;

public record TotpSetup(string SecretBase32, string OtpAuthUri, string QrCodePngBase64);

public interface ITotpService
{
    /// <summary>Generates a new random secret + the otpauth:// URI + a QR code PNG
    /// (base64) the user scans with Google Authenticator (or any TOTP app).</summary>
    TotpSetup GenerateSetup(string accountLabel, string issuer = "Homelab Dashboard");

    /// <summary>Verifies a 6-digit code against the secret. Allows one 30s step of
    /// clock drift each direction, matching Google Authenticator's own tolerance.</summary>
    bool VerifyCode(string secretBase32, string code);
}

public class TotpService : ITotpService
{
    public TotpSetup GenerateSetup(string accountLabel, string issuer = "Homelab Dashboard")
    {
        var secretBytes = KeyGeneration.GenerateRandomKey(20); // 160 bits, standard TOTP secret size
        var secretBase32 = Base32Encoding.ToString(secretBytes);

        var otpAuthUri =
            $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(accountLabel)}" +
            $"?secret={secretBase32}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";

        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(otpAuthUri, QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(qrData);
        var qrBytes = pngQrCode.GetGraphic(10);

        return new TotpSetup(secretBase32, otpAuthUri, Convert.ToBase64String(qrBytes));
    }

    public bool VerifyCode(string secretBase32, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var secretBytes = Base32Encoding.ToBytes(secretBase32);
        var totp = new Totp(secretBytes, step: 30);

        return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
    }
}
