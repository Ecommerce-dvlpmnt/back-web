using System.Text.Json;
using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace EcommerceBackend.Application.Services;

public sealed class ProductService(
    IProductRepository productRepository,
    IImageStorageService imageStorageService) : IProductService
{
    public async Task<PagedResult<ProductDto>> GetAllAsync(
        ProductQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        var result = await productRepository.GetAllAsync(query, cancellationToken);
        return new PagedResult<ProductDto>
        {
            Items = result.Items.Select(MapToDto).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        };
    }

    public async Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        return product is null ? null : MapToDto(product);
    }

    public async Task<ProductDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetBySlugAsync(slug, cancellationToken);
        return product is null ? null : MapToDto(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductDto request, CancellationToken cancellationToken = default)
    {
        await EnsureUniqueAsync(request.Sku, request.Slug, null, cancellationToken);
        var now = DateTime.UtcNow;
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = request.CategoryId,
            SupplierId = request.SupplierId,
            Sku = request.Sku,
            Name = request.Name,
            Slug = request.Slug,
            Description = request.Description,
            DropPrice = request.DropPrice,
            Price = request.Price,
            OldPrice = request.OldPrice,
            Stock = request.Stock,
            MainImageUrl = request.MainImageUrl,
            Attributes = NormalizeAttributes(request.Attributes),
            MetaTitle = request.MetaTitle,
            MetaDescription = request.MetaDescription,
            WeightKg = request.WeightKg,
            IsActive = request.IsActive,
            CreatedAt = now,
            UpdatedAt = now,
            Images = MapImages(request.Images)
        };

        return MapToDto(await productRepository.CreateAsync(product, cancellationToken));
    }

    public async Task<ProductDto?> UpdateAsync(
        Guid id,
        UpdateProductDto request,
        CancellationToken cancellationToken = default)
    {
        await EnsureUniqueAsync(request.Sku, request.Slug, id, cancellationToken);
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        if (product is null)
            return null;

        product.CategoryId = request.CategoryId;
        product.SupplierId = request.SupplierId;
        product.Sku = request.Sku;
        product.Name = request.Name;
        product.Slug = request.Slug;
        product.Description = request.Description;
        product.DropPrice = request.DropPrice;
        product.Price = request.Price;
        product.OldPrice = request.OldPrice;
        product.Stock = request.Stock;
        product.MainImageUrl = request.MainImageUrl;
        product.Attributes = NormalizeAttributes(request.Attributes);
        product.MetaTitle = request.MetaTitle;
        product.MetaDescription = request.MetaDescription;
        product.WeightKg = request.WeightKg;
        product.IsActive = request.IsActive;
        product.Images = MapImages(request.Images);

        var updated = await productRepository.UpdateAsync(product, cancellationToken);
        return updated is null ? null : MapToDto(updated);
    }

    public async Task<ProductDto> UploadImageToProductAsync(
        Guid productId,
        IFormFile file,
        bool isMainImage,
        CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(productId, cancellationToken)
            ?? throw new KeyNotFoundException($"Product '{productId}' was not found.");

        var imageUrl = await imageStorageService.UploadImageAsync(file, "products", cancellationToken);
        ProductImage? image = null;
        string? mainImageUrl = null;

        if (isMainImage)
        {
            mainImageUrl = imageUrl;
        }
        else
        {
            image = new ProductImage
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                Url = imageUrl,
                SortOrder = product.Images.Count == 0
                    ? 0
                    : product.Images.Max(existingImage => existingImage.SortOrder) + 1
            };
        }

        var updatedProduct = await productRepository.UpdateImageAsync(
            productId,
            mainImageUrl,
            image,
            cancellationToken)
            ?? throw new KeyNotFoundException($"Product '{productId}' was not found.");

        return MapToDto(updatedProduct);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        productRepository.DeleteAsync(id, cancellationToken);

    public Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        productRepository.SoftDeleteAsync(id, cancellationToken);

    public Task<bool> UpdateStockAsync(Guid id, int stock, CancellationToken cancellationToken = default) =>
        productRepository.UpdateStockAsync(id, stock, cancellationToken);

    public Task<bool> ExistsBySkuOrSlugAsync(
        string sku,
        string slug,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        productRepository.ExistsBySkuOrSlugAsync(sku, slug, excludeId, cancellationToken);

    private async Task EnsureUniqueAsync(
        string sku,
        string slug,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        if (await ExistsBySkuOrSlugAsync(sku, slug, excludeId, cancellationToken))
            throw new InvalidOperationException("A product with this SKU or slug already exists.");
    }

    private static string NormalizeAttributes(string? attributes)
    {
        if (string.IsNullOrWhiteSpace(attributes))
            return "{}";

        try
        {
            using var _ = JsonDocument.Parse(attributes);
            return attributes;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Attributes must contain valid JSON.", nameof(attributes), exception);
        }
    }

    private static ICollection<ProductImage> MapImages(IEnumerable<CreateProductImageDto>? images) =>
        images?.Select(image => new ProductImage
        {
            Id = Guid.NewGuid(),
            Url = image.Url,
            Alt = image.Alt,
            SortOrder = image.SortOrder
        }).ToList() ?? [];

    private static ProductDto MapToDto(Product product) => new()
    {
        Id = product.Id,
        CategoryId = product.CategoryId,
        CategoryName = product.Category?.Name,
        SupplierId = product.SupplierId,
        Sku = product.Sku,
        Name = product.Name,
        Slug = product.Slug,
        Description = product.Description,
        DropPrice = product.DropPrice,
        Price = product.Price,
        OldPrice = product.OldPrice,
        Stock = product.Stock,
        MainImageUrl = product.MainImageUrl,
        Attributes = product.Attributes,
        MetaTitle = product.MetaTitle,
        MetaDescription = product.MetaDescription,
        WeightKg = product.WeightKg,
        IsActive = product.IsActive,
        CreatedAt = product.CreatedAt,
        UpdatedAt = product.UpdatedAt,
        Images = product.Images
            .OrderBy(image => image.SortOrder)
            .Select(image => new ProductImageDto
            {
                Id = image.Id,
                Url = image.Url,
                Alt = image.Alt,
                SortOrder = image.SortOrder
            })
            .ToList()
    };
}
