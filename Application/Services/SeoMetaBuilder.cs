using System.Text.RegularExpressions;

namespace EcommerceBackend.Application.Services;

/// <summary>
/// Формує SEO-безпечні MetaTitle, MetaDescription та Alt-теги українською мовою,
/// не перезаписуючи ручні налаштування адміністратора в БД.
/// </summary>
public static partial class SeoMetaBuilder
{
    public const string BrandName = "Jingle Decor";
    private const int MaxMetaTitleLength = 68;
    private const int MaxMetaDescriptionLength = 160;

    [GeneratedRegex("<[^>]*>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?:^|\s+)Відеоогляд$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TrailingVideoLabelRegex();

    public static string StripHtmlAndNormalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var withoutTags = HtmlTagRegex().Replace(value, " ");
        var normalized = WhitespaceRegex().Replace(withoutTags, " ").Trim();
        return TrailingVideoLabelRegex().Replace(normalized, string.Empty).Trim();
    }

    public static string BuildProductMetaTitle(string? productName, string? customMetaTitle)
    {
        if (!string.IsNullOrWhiteSpace(customMetaTitle))
            return customMetaTitle.Trim();

        var cleanName = WhitespaceRegex().Replace(productName ?? string.Empty, " ").Trim();
        if (string.IsNullOrEmpty(cleanName))
            return $"Святковий декор — купити | {BrandName}";

        var fullCandidate = $"{cleanName} — купити в Україні | {BrandName}";
        if (fullCandidate.Length <= MaxMetaTitleLength)
            return fullCandidate;

        var mediumCandidate = $"{cleanName} — купити | {BrandName}";
        if (mediumCandidate.Length <= MaxMetaTitleLength)
            return mediumCandidate;

        var shortCandidate = $"{cleanName} | {BrandName}";
        if (shortCandidate.Length <= MaxMetaTitleLength)
            return shortCandidate;

        return TruncateAtWordBoundary(cleanName, MaxMetaTitleLength - ($" | {BrandName}").Length) + $" | {BrandName}";
    }

    public static string BuildProductMetaDescription(
        string? productName,
        string? description,
        decimal price,
        string? customMetaDescription)
    {
        if (!string.IsNullOrWhiteSpace(customMetaDescription))
            return WhitespaceRegex().Replace(customMetaDescription, " ").Trim();

        var cleanName = WhitespaceRegex().Replace(productName ?? string.Empty, " ").Trim();
        var cleanDesc = StripHtmlAndNormalize(description);
        var priceText = price > 0m ? $"{decimal.Round(price, 0, MidpointRounding.AwayFromZero):0} грн" : null;

        if (!string.IsNullOrEmpty(cleanDesc))
        {
            var suffix = priceText is not null
                ? $" Ціна {priceText}. Доставка Новою Поштою по Україні — {BrandName}."
                : $" Доставка Новою Поштою по Україні — {BrandName}.";

            if (cleanDesc.Length + suffix.Length <= MaxMetaDescriptionLength)
                return cleanDesc + suffix;

            var maxBodyLength = MaxMetaDescriptionLength - suffix.Length;
            if (maxBodyLength >= 60)
            {
                var truncatedBody = TruncateAtWordBoundary(cleanDesc, maxBodyLength).TrimEnd('.', ',', ';', ':', '-', '—');
                return $"{truncatedBody}…{suffix}";
            }

            return TruncateAtWordBoundary(cleanDesc, MaxMetaDescriptionLength);
        }

        if (!string.IsNullOrEmpty(cleanName))
        {
            var fallback = priceText is not null
                ? $"Купити {cleanName} за ціною {priceText} в інтернет-магазині {BrandName}. Швидка доставка Новою Поштою 1–2 дні, оплата при отриманні."
                : $"Купити {cleanName} в інтернет-магазині {BrandName}. Швидка доставка Новою Поштою по всій Україні, оплата при отриманні.";

            return TruncateAtWordBoundary(fallback, MaxMetaDescriptionLength);
        }

        return $"Купити литі ялинки, різдвяні вінки та святковий декор в інтернет-магазині {BrandName} з доставкою по Україні.";
    }

    public static string BuildCategoryMetaTitle(string? categoryName, string? customMetaTitle)
    {
        if (!string.IsNullOrWhiteSpace(customMetaTitle))
            return customMetaTitle.Trim();

        var cleanName = WhitespaceRegex().Replace(categoryName ?? string.Empty, " ").Trim();
        if (string.IsNullOrEmpty(cleanName))
            return $"Каталог святкового декору | {BrandName}";

        var fullCandidate = $"{cleanName} — купити в Україні | {BrandName}";
        if (fullCandidate.Length <= MaxMetaTitleLength)
            return fullCandidate;

        return $"{TruncateAtWordBoundary(cleanName, MaxMetaTitleLength - ($" | {BrandName}").Length)} | {BrandName}";
    }

    public static string BuildCategoryMetaDescription(string? categoryName, string? customMetaDescription)
    {
        if (!string.IsNullOrWhiteSpace(customMetaDescription))
            return WhitespaceRegex().Replace(customMetaDescription, " ").Trim();

        var cleanName = WhitespaceRegex().Replace(categoryName ?? string.Empty, " ").Trim();
        if (string.IsNullOrEmpty(cleanName))
            return $"Штучні литі ялинки, різдвяні вінки та святковий декор від українського бренду {BrandName}. Доставка Новою Поштою 1–2 дні.";

        var generated = $"{cleanName} від українського бренду {BrandName}. Великий вибір, актуальні ціни, швидка доставка Новою Поштою 1–2 дні та оплата при отриманні.";
        return TruncateAtWordBoundary(generated, MaxMetaDescriptionLength);
    }

    public static string BuildImageAlt(string? productName, string? customAlt, int index)
    {
        if (!string.IsNullOrWhiteSpace(customAlt))
            return customAlt.Trim();

        var cleanName = WhitespaceRegex().Replace(productName ?? string.Empty, " ").Trim();
        if (string.IsNullOrEmpty(cleanName))
            return BrandName;

        return index <= 0 ? cleanName : $"{cleanName} — фото {index + 1}";
    }

    public static string TruncateAtWordBoundary(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || maxLength <= 0)
            return string.Empty;

        if (text.Length <= maxLength)
            return text;

        var limit = Math.Max(1, maxLength - 1);
        var lastSpace = text.LastIndexOf(' ', limit);
        var slice = lastSpace > limit / 2 ? text[..lastSpace] : text[..limit];
        return $"{slice.TrimEnd(' ', '.', ',', ';', ':', '-', '—')}…";
    }
}
