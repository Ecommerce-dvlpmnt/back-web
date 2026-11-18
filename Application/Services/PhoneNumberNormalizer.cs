using System.Text;

namespace EcommerceBackend.Application.Services;

public static class PhoneNumberNormalizer
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        var plusIndex = trimmed.IndexOf('+');
        if (plusIndex > 0 || (plusIndex == 0 && trimmed.IndexOf('+', 1) >= 0))
            return false;

        var digits = new StringBuilder(trimmed.Length);
        foreach (var character in trimmed)
        {
            if (char.IsAsciiDigit(character))
            {
                digits.Append(character);
                continue;
            }

            if (character is '+' or '-' or '(' or ')' or '.' || char.IsWhiteSpace(character))
                continue;

            return false;
        }

        var digitsOnly = digits.ToString();
        var candidate = digitsOnly switch
        {
            { Length: 10 } when digitsOnly[0] == '0' => "+38" + digitsOnly,
            { Length: 11 } when digitsOnly.StartsWith("80", StringComparison.Ordinal) => "+3" + digitsOnly,
            { Length: 12 } when digitsOnly.StartsWith("380", StringComparison.Ordinal) => "+" + digitsOnly,
            _ => string.Empty
        };

        if (candidate.Length != 13)
            return false;

        var nationalNumber = candidate.AsSpan(4);
        if (nationalNumber[0] is < '3' or > '9')
            return false;

        normalized = candidate;
        return true;
    }
}
