using EcommerceBackend.Application.Security;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class DataMaskingTests
{
    [Theory]
    [InlineData("password", true)]
    [InlineData("Password", true)]
    [InlineData("jwtSecret", true)]
    [InlineData("NovaPoshta:ApiKey", true)]
    [InlineData("authToken", true)]
    [InlineData("creditCardNumber", true)]
    [InlineData("name", false)]
    [InlineData("categoryTitle", false)]
    [InlineData("orderId", false)]
    public void IsSensitiveKey_IdentifiesSensitivePropertyNames(string key, bool expected)
    {
        var result = DataMasking.IsSensitiveKey(key);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("secret123456", "sec***[REDACTED]")]
    [InlineData("super-secret-key-for-jwt", "sup***[REDACTED]")]
    [InlineData("abc", "***")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MaskSecret_RedactsSecretValues(string? input, string expected)
    {
        var result = DataMasking.MaskSecret(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("+380971234567", "+380***4567")]
    [InlineData("0971234567", "097***4567")]
    [InlineData("123", "***")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MaskPhone_MasksMiddleDigits(string? phone, string expected)
    {
        var result = DataMasking.MaskPhone(phone);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("user@example.com", "u***r@example.com")]
    [InlineData("alexander.smith@gmail.com", "a***h@gmail.com")]
    [InlineData("ab@test.com", "a***@test.com")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void MaskEmail_MasksEmailUsername(string? email, string expected)
    {
        var result = DataMasking.MaskEmail(email);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ConvertPostgresUriToConnectionString_ParsesStandardPostgresUrl()
    {
        var url = "postgres://postgres.myproject:mySecretPass123@aws-1-eu-central-1.pooler.supabase.com:6543/postgres";
        var connStr = DatabaseConnectionHelper.ConvertPostgresUriToConnectionString(url);

        Assert.Contains("Host=aws-1-eu-central-1.pooler.supabase.com", connStr);
        Assert.Contains("Port=6543", connStr);
        Assert.Contains("Database=postgres", connStr);
        Assert.Contains("Username=postgres.myproject", connStr);
        Assert.Contains("Password=mySecretPass123", connStr);
        Assert.Contains("SSL Mode=Require", connStr);
    }
}
