using System.Text.RegularExpressions;

namespace EcommerceBackend.Application.Security;

/// <summary>
/// Утилиты маскирования персональных данных (PII) и секретных ключей для безопасного вывода в логи и API.
/// </summary>
public static partial class DataMasking
{
    private static readonly HashSet<string> SensitiveKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "secret", "token", "apikey", "api_key", "authorization",
        "authtoken", "jwt", "creditcard", "pan", "cvv", "cvv2", "pin"
    };

    private static readonly HashSet<string> PersonalDataKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "phone", "customerphone", "recipientphone", "email", "customeremail"
    };

    /// <summary>
    /// Проверяет, является ли имя свойства чувствительным ключом (пароль, секрет, токен, API-ключ).
    /// </summary>
    public static bool IsSensitiveKey(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return false;

        var clean = propertyName.Trim().ToLowerInvariant();
        return SensitiveKeyNames.Any(key => clean.Contains(key));
    }

    /// <summary>
    /// Проверяет, является ли имя свойства персональными данными (телефон, email).
    /// </summary>
    public static bool IsPersonalDataKey(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return false;

        var clean = propertyName.Trim().ToLowerInvariant();
        return PersonalDataKeyNames.Any(key => clean.Contains(key));
    }

    /// <summary>
    /// Маскирует секретное значение (API-ключ, пароль, токен).
    /// </summary>
    public static string MaskSecret(string? secret, int visiblePrefix = 3)
    {
        if (string.IsNullOrEmpty(secret))
            return string.Empty;

        if (secret.Length <= visiblePrefix)
            return "***";

        return $"{secret[..visiblePrefix]}***[REDACTED]";
    }

    /// <summary>
    /// Маскирует номер телефона: оставляет код страны и последние 4 цифры, скрывая середину (+380***1234).
    /// </summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        var trimmed = phone.Trim();
        if (trimmed.Length < 7)
            return "***";

        var prefixLen = trimmed.StartsWith('+') ? 4 : 3;
        if (trimmed.Length <= prefixLen + 4)
            return "***" + trimmed[^3..];

        var prefix = trimmed[..prefixLen];
        var suffix = trimmed[^4..];
        return $"{prefix}***{suffix}";
    }

    /// <summary>
    /// Маскирует email адрес: оставляет первую букву логина и домен (e.g. u***r@domain.com).
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        var atIndex = email.IndexOf('@');
        if (atIndex <= 1)
            return "***" + (atIndex >= 0 ? email[atIndex..] : "");

        var namePart = email[..atIndex];
        var domainPart = email[atIndex..];

        if (namePart.Length <= 2)
            return $"{namePart[0]}***{domainPart}";

        return $"{namePart[0]}***{namePart[^1]}{domainPart}";
    }

    /// <summary>
    /// Универсальное маскирование значения в зависимости от имени свойства.
    /// </summary>
    public static object? SanitizeValue(string propertyName, object? value)
    {
        if (value is null)
            return null;

        var str = value.ToString();
        if (string.IsNullOrWhiteSpace(str))
            return value;

        if (IsSensitiveKey(propertyName))
            return MaskSecret(str);

        if (propertyName.Contains("phone", StringComparison.OrdinalIgnoreCase))
            return MaskPhone(str);

        if (propertyName.Contains("email", StringComparison.OrdinalIgnoreCase))
            return MaskEmail(str);

        return value;
    }
}
