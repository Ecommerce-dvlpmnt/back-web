using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Domain.Entities;

namespace EcommerceBackend.Application.Interfaces;

public interface IProductRepository
{
    Task<PagedResult<Product>> GetAllAsync(ProductQueryParameters query, CancellationToken cancellationToken = default);
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default);
    Task<Product?> UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task<Product?> UpdateImageAsync(
        Guid productId,
        string? mainImageUrl,
        ProductImage? image,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> UpdateStockAsync(Guid id, int stock, CancellationToken cancellationToken = default);
    Task<bool> ExistsBySkuOrSlugAsync(string sku, string slug, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
