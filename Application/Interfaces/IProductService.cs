using EcommerceBackend.Application.DTOs.Products;
using Microsoft.AspNetCore.Http;

namespace EcommerceBackend.Application.Interfaces;

public interface IProductService
{
    Task<PagedResult<ProductDto>> GetAllAsync(ProductQueryParameters query, CancellationToken cancellationToken = default);
    Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProductDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<ProductDto> CreateAsync(CreateProductDto product, CancellationToken cancellationToken = default);
    Task<ProductDto?> UpdateAsync(Guid id, UpdateProductDto product, CancellationToken cancellationToken = default);
    Task<ProductDto> UploadImageToProductAsync(
        Guid productId,
        IFormFile file,
        bool isMainImage,
        CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsBySkuOrSlugAsync(string sku, string slug, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
