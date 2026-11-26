using System.Text.Json;
using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.AspNetCore.Http;

namespace EcommerceBackend.Application.Services;

public sealed class ProductService(
    IProductRepository productRepository,
    IImageStorageService imageStorageService,
    IAuditLogService auditLogService) : IProductService
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

    public async Task<PagedResult<PublicProductDto>> GetPublicAllAsync(
        ProductQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        query.OnlyActive = true;
        query.SupplierId = null;
        var result = await productRepository.GetAllAsync(query, cancellationToken);
        return new PagedResult<PublicProductDto>
        {
            Items = result.Items.Select(MapToPublicDto).ToList(),
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

    public async Task<PublicProductDto?> GetPublicByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        if (product is null || !product.IsActive)
            return null;

        return MapToPublicDto(product);
    }

    public async Task<ProductDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetBySlugAsync(slug, cancellationToken);
        return product is null ? null : MapToDto(product);
    }

    public async Task<PublicProductDto?> GetPublicBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetBySlugAsync(slug, cancellationToken);
        if (product is null || !product.IsActive)
            return null;

        return MapToPublicDto(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductDto request, CancellationToken cancellationToken = default)
    {
        ValidateProductValues(request);
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
            Attributes = NormalizeAttributes(request.Attributes, request.IsRecommended, request.ShowOnHomepage),
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
        ValidateProductValues(request);
        await EnsureUniqueAsync(request.Sku, request.Slug, id, cancellationToken);
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        if (product is null)
            return null;

        var oldPrice = product.Price;
        var oldStock = product.Stock;

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
        product.Attributes = NormalizeAttributes(request.Attributes, request.IsRecommended, request.ShowOnHomepage);
        product.MetaTitle = request.MetaTitle;
        product.MetaDescription = request.MetaDescription;
        product.WeightKg = request.WeightKg;
        product.IsActive = request.IsActive;
        product.Images = MapImages(request.Images);

        if (oldPrice != product.Price || oldStock != product.Stock)
        {
            var changedFields = new List<string>(2);
            if (oldPrice != product.Price)
                changedFields.Add("price");
            if (oldStock != product.Stock)
                changedFields.Add("stock");

            await auditLogService.LogAsync(
                AuditLogActions.ProductUpdated,
                AuditLogEntityTypes.Product,
                product.Id.ToString(),
                new
                {
                    sku = product.Sku,
                    name = product.Name,
                    before = new { price = oldPrice, stock = oldStock },
                    after = new { price = product.Price, stock = product.Stock },
                    changedFields = changedFields.ToArray()
                },
                cancellationToken);
        }

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
            ?? throw new KeyNotFoundException($"Товар з ID '{productId}' не знайдено.");

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
            ?? throw new KeyNotFoundException($"Товар з ID '{productId}' не знайдено.");

        return MapToDto(updatedProduct);
    }

    public async Task<ProductDto?> UpdateStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        if (product is null)
            return null;

        if (product.IsActive != isActive)
        {
            await auditLogService.LogAsync(
                isActive ? AuditLogActions.ProductActivated : AuditLogActions.ProductDeactivated,
                AuditLogEntityTypes.Product,
                product.Id.ToString(),
                new
                {
                    sku = product.Sku,
                    name = product.Name,
                    before = new { isActive = product.IsActive },
                    after = new { isActive }
                },
                cancellationToken);
        }

        product.IsActive = isActive;
        var updated = await productRepository.UpdateAsync(product, cancellationToken);
        return updated is null ? null : MapToDto(updated);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        if (product is null)
            return false;

        if (await productRepository.HasOrdersAsync(id, cancellationToken))
            throw new InvalidOperationException("Неможливо видалити товар, за яким вже є оформлені замовлення. Деактивуйте його замість видалення.");

        await auditLogService.LogAsync(
            AuditLogActions.ProductDeleted,
            AuditLogEntityTypes.Product,
            product.Id.ToString(),
            new
            {
                sku = product.Sku,
                name = product.Name
            },
            cancellationToken);

        return await productRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        if (product is null)
            return false;

        if (product.IsActive)
        {
            await auditLogService.LogAsync(
                AuditLogActions.ProductDeactivated,
                AuditLogEntityTypes.Product,
                product.Id.ToString(),
                new
                {
                    sku = product.Sku,
                    name = product.Name,
                    softDelete = true,
                    before = new { isActive = true },
                    after = new { isActive = false }
                },
                cancellationToken);
        }

        return await productRepository.SoftDeleteAsync(id, cancellationToken);
    }

    public async Task<bool> UpdateStockAsync(
        Guid id,
        int stock,
        CancellationToken cancellationToken = default)
    {
        var product = await productRepository.GetByIdAsync(id, cancellationToken);
        if (product is null)
            return false;

        if (product.Stock != stock)
        {
            await auditLogService.LogAsync(
                AuditLogActions.ProductStockUpdated,
                AuditLogEntityTypes.Product,
                product.Id.ToString(),
                new
                {
                    sku = product.Sku,
                    name = product.Name,
                    before = new { stock = product.Stock },
                    after = new { stock },
                    changedFields = new[] { "stock" }
                },
                cancellationToken);
        }

        return await productRepository.UpdateStockAsync(id, stock, cancellationToken);
    }

    public Task<bool> ExistsBySkuOrSlugAsync(
        string sku,
        string slug,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        productRepository.ExistsBySkuOrSlugAsync(sku, slug, excludeId, cancellationToken);

    private static void ValidateProductValues(CreateProductDto request)
    {
        if (request.Price <= 0m)
        {
            throw new ArgumentException("Ціна продажу (Price) повинна бути більшою за 0.", nameof(request));
        }

        if (request.DropPrice < 0m)
        {
            throw new ArgumentException("Закупівельна ціна (DropPrice) не може бути від'ємною.", nameof(request));
        }

        if (request.DropPrice > request.Price)
        {
            throw new ArgumentException("Закупівельна ціна (DropPrice) не може перевищувати роздрібну ціну продажу (Price).", nameof(request));
        }

        if (request.OldPrice.HasValue && request.OldPrice.Value <= 0m)
        {
            throw new ArgumentException("Стара ціна (OldPrice) повинна бути більшою за 0.", nameof(request));
        }

        if (request.WeightKg.HasValue && request.WeightKg.Value < 0m)
        {
            throw new ArgumentException("Вага товару (WeightKg) не може бути від'ємною.", nameof(request));
        }

        if (request.Stock < 0)
        {
            throw new ArgumentException("Залишок на складі (Stock) не може бути від'ємним.", nameof(request));
        }
    }

    private async Task EnsureUniqueAsync(
        string sku,
        string slug,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        if (await ExistsBySkuOrSlugAsync(sku, slug, excludeId, cancellationToken))
            throw new InvalidOperationException("Товар з таким артикулом (SKU) або slug вже існує.");
    }

    private static string NormalizeAttributes(
        string? attributes,
        bool? isRecommended = null,
        bool? showOnHomepage = null)
    {
        System.Text.Json.Nodes.JsonObject root;
        if (string.IsNullOrWhiteSpace(attributes))
        {
            root = new System.Text.Json.Nodes.JsonObject();
        }
        else
        {
            try
            {
                var parsed = System.Text.Json.Nodes.JsonNode.Parse(attributes);
                if (parsed is System.Text.Json.Nodes.JsonObject obj)
                {
                    root = obj;
                }
                else if (isRecommended is null && showOnHomepage is null)
                {
                    return attributes;
                }
                else
                {
                    throw new ArgumentException("Атрибути повинні містити JSON-об'єкт.", nameof(attributes));
                }
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Атрибути повинні містити валідний JSON.", nameof(attributes), exception);
            }
        }

        if (isRecommended.HasValue)
        {
            if (isRecommended.Value)
                root["is_recommended"] = true;
            else
                root.Remove("is_recommended");
        }

        if (showOnHomepage.HasValue)
        {
            if (showOnHomepage.Value)
                root["show_on_homepage"] = true;
            else
                root.Remove("show_on_homepage");
        }

        return isRecommended.HasValue || showOnHomepage.HasValue || string.IsNullOrWhiteSpace(attributes)
            ? root.ToJsonString()
            : attributes;
    }

    private static bool ReadAttributeFlag(string? attributesJson, string key)
    {
        if (string.IsNullOrWhiteSpace(attributesJson))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(attributesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            if (!doc.RootElement.TryGetProperty(key, out var prop))
                return false;

            return prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.String => string.Equals(prop.GetString(), "true", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
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

    private static ProductDto MapToDto(Product product)
    {
        var (averageRating, reviewCount) = ComputeApprovedRating(product.Reviews);

        return new ProductDto
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
            AverageRating = averageRating,
            ReviewCount = reviewCount,
            IsActive = product.IsActive,
            IsRecommended = ReadAttributeFlag(product.Attributes, "is_recommended"),
            ShowOnHomepage = ReadAttributeFlag(product.Attributes, "show_on_homepage"),
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
                .ToList(),
            Variants = MapVariants(product)
        };
    }

    private static PublicProductDto MapToPublicDto(Product product)
    {
        var (averageRating, reviewCount) = ComputeApprovedRating(product.Reviews);

        return new PublicProductDto
        {
            Id = product.Id,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name,
            Sku = product.Sku,
            Name = product.Name,
            Slug = product.Slug,
            Description = product.Description,
            Price = product.Price,
            OldPrice = product.OldPrice,
            Stock = product.Stock,
            MainImageUrl = product.MainImageUrl,
            Attributes = product.Attributes,
            MetaTitle = SeoMetaBuilder.BuildProductMetaTitle(product.Name, product.MetaTitle),
            MetaDescription = SeoMetaBuilder.BuildProductMetaDescription(
                product.Name,
                product.Description,
                product.Price,
                product.MetaDescription),
            WeightKg = product.WeightKg,
            AverageRating = averageRating,
            ReviewCount = reviewCount,
            IsActive = product.IsActive,
            IsRecommended = ReadAttributeFlag(product.Attributes, "is_recommended"),
            ShowOnHomepage = ReadAttributeFlag(product.Attributes, "show_on_homepage"),
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            Images = product.Images
                .OrderBy(image => image.SortOrder)
                .Select((image, index) => new ProductImageDto
                {
                    Id = image.Id,
                    Url = image.Url,
                    Alt = SeoMetaBuilder.BuildImageAlt(product.Name, image.Alt, index),
                    SortOrder = image.SortOrder
                })
                .ToList(),
            Variants = MapVariants(product)
        };
    }

    private static (decimal? AverageRating, int ReviewCount) ComputeApprovedRating(ICollection<Review>? reviews)
    {
        if (reviews is null || reviews.Count == 0)
            return (null, 0);

        var approved = reviews.Where(r => r.Status == ReviewStatus.Approved).ToList();
        if (approved.Count == 0)
            return (null, 0);

        var avg = decimal.Round((decimal)approved.Average(r => r.Rating), 1, MidpointRounding.AwayFromZero);
        return (avg, approved.Count);
    }

    private static List<ProductVariantDto> MapVariants(Product product)
    {
        if (product.RelatedLinks is null || product.RelatedLinks.Count == 0)
            return [];

        var all = new List<Product> { product };
        all.AddRange(product.RelatedLinks
            .Select(r => r.RelatedProduct)
            .Where(p => p is not null && p.IsActive));

        var distinct = all.DistinctBy(p => p.Id).ToList();
        if (distinct.Count <= 1)
            return [];

        return distinct
            .Select(p =>
            {
                var h = ExtractHeight(p.Name, p.Attributes);
                return new ProductVariantDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Slug = p.Slug,
                    Price = p.Price,
                    OldPrice = p.OldPrice,
                    Height = string.IsNullOrWhiteSpace(h) ? p.Name : h,
                    InStock = p.Stock > 0,
                    IsCurrent = p.Id == product.Id
                };
            })
            .OrderBy(v => ParseHeightValue(v.Height))
            .ToList();
    }

    private static string ExtractHeight(string name, string? attributesJson)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d+([.,]\d+)?)\s*(м|см)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Value;

        if (!string.IsNullOrWhiteSpace(attributesJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(attributesJson);
                if (doc.RootElement.TryGetProperty("Висота", out var hProp))
                {
                    var hStr = hProp.GetString();
                    if (!string.IsNullOrWhiteSpace(hStr))
                    {
                        var hMatch = System.Text.RegularExpressions.Regex.Match(hStr, @"(\d+([.,]\d+)?)\s*(м|см)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (hMatch.Success) return hMatch.Value;
                        return hStr;
                    }
                }
            }
            catch { }
        }

        return string.Empty;
    }

    private static decimal ParseHeightValue(string height)
    {
        var m = System.Text.RegularExpressions.Regex.Match(height, @"(\d+([.,]\d+)?)");
        if (m.Success && decimal.TryParse(m.Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var val))
        {
            if (height.Contains("см", StringComparison.OrdinalIgnoreCase))
                return val / 100m;
            return val;
        }
        return 0m;
    }
}
