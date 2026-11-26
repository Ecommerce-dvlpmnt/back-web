namespace EcommerceBackend.Application.DTOs.Seo;

public sealed class SeoStructuredDataResponse
{
    public string MetaTitle { get; init; } = string.Empty;
    public string MetaDescription { get; init; } = string.Empty;
    public string CanonicalUrl { get; init; } = string.Empty;
    public object JsonLd { get; init; } = null!;
}
