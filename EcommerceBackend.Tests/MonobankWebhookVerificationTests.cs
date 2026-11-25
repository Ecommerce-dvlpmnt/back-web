using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class MonobankWebhookVerificationTests
{
    [Fact]
    public void VerifyEcdsaSignature_ReturnsTrue_ForDerEncodedSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pubKeyPem = key.ExportSubjectPublicKeyInfoPem();
        var base64Pem = Convert.ToBase64String(Encoding.UTF8.GetBytes(pubKeyPem));

        var body = "{\"invoiceId\":\"test-123\",\"status\":\"success\",\"amount\":10000}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        var sigBytes = key.SignData(bodyBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var xSign = Convert.ToBase64String(sigBytes);

        // Verify using base64 encoded PEM (Monobank API format)
        var isValid = Verify(bodyBytes, xSign, base64Pem);
        Assert.True(isValid);

        // Verify using plain PEM
        var isValidPlain = Verify(bodyBytes, xSign, pubKeyPem);
        Assert.True(isValidPlain);
    }

    [Fact]
    public void VerifyEcdsaSignature_ReturnsTrue_ForIeeeP1363Signature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pubKeyPem = key.ExportSubjectPublicKeyInfoPem();

        var body = "{\"invoiceId\":\"test-456\",\"status\":\"failure\",\"amount\":5000}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);

        var sigBytes = key.SignData(bodyBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var xSign = Convert.ToBase64String(sigBytes);

        var isValid = Verify(bodyBytes, xSign, pubKeyPem);
        Assert.True(isValid);
    }

    [Fact]
    public void VerifyEcdsaSignature_ReturnsFalse_ForTamperedBody()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pubKeyPem = key.ExportSubjectPublicKeyInfoPem();

        var originalBody = "{\"invoiceId\":\"test-123\",\"status\":\"success\",\"amount\":10000}";
        var tamperedBody = "{\"invoiceId\":\"test-123\",\"status\":\"success\",\"amount\":100}";

        var sigBytes = key.SignData(Encoding.UTF8.GetBytes(originalBody), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        var xSign = Convert.ToBase64String(sigBytes);

        var isValid = Verify(Encoding.UTF8.GetBytes(tamperedBody), xSign, pubKeyPem);
        Assert.False(isValid);
    }

    private static bool Verify(byte[] bodyBytes, string xSignBase64, string keyString)
    {
        try
        {
            var sig = Convert.FromBase64String(xSignBase64.Trim());
            using var ecdsa = ECDsa.Create();
            var candidate = keyString.Trim();

            if (!candidate.Contains("BEGIN PUBLIC KEY"))
            {
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(candidate));
                    if (decoded.Contains("BEGIN PUBLIC KEY"))
                        candidate = decoded;
                }
                catch { }
            }

            if (candidate.Contains("BEGIN PUBLIC KEY"))
            {
                ecdsa.ImportFromPem(candidate);
            }
            else
            {
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(candidate), out _);
            }

            if (ecdsa.VerifyData(bodyBytes, sig, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                return true;

            if (ecdsa.VerifyData(bodyBytes, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }
}
