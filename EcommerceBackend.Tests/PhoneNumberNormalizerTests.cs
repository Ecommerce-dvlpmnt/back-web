using EcommerceBackend.Application.Services;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("+380991234567", "+380991234567")]
    [InlineData("0991234567", "+380991234567")]
    [InlineData("380991234567", "+380991234567")]
    [InlineData("80 991 234 56 7", "+380991234567")]
    [InlineData("+38 (099) 123-45-67", "+380991234567")]
    [InlineData("  099 123 45 67  ", "+380991234567")]
    [InlineData("+380441234567", "+380441234567")]
    public void TryNormalize_AcceptsUkrainianNumbers(string input, string expected)
    {
        var result = PhoneNumberNormalizer.TryNormalize(input, out var normalized);

        Assert.True(result);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("09912345")]
    [InlineData("09912345678")]
    [InlineData("+38099123456")]
    [InlineData("+380000000000")]
    [InlineData("+3809912345678")]
    [InlineData("0+991234567")]
    [InlineData("++380991234567")]
    [InlineData("380 99 123 45 67 доб.")]
    public void TryNormalize_RejectsInvalidNumbers(string? input)
    {
        var result = PhoneNumberNormalizer.TryNormalize(input, out var normalized);

        Assert.False(result);
        Assert.Equal(string.Empty, normalized);
    }
}
