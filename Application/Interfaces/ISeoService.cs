using EcommerceBackend.Application.DTOs.Seo;

namespace EcommerceBackend.Application.Interfaces;

public interface ISeoService
{
    Task<string> GetLlmsTxtAsync(CancellationToken cancellationToken = default);
    Task<string> GetLlmsFullTxtAsync(CancellationToken cancellationToken = default);
    SeoStructuredDataResponse GetHomeStructuredData();
    Task<SeoStructuredDataResponse?> GetCategoryStructuredDataAsync(string slug, CancellationToken cancellationToken = default);
    Task<SeoStructuredDataResponse?> GetProductStructuredDataAsync(string slug, CancellationToken cancellationToken = default);
}
