using System.ComponentModel.DataAnnotations;

namespace EcommerceBackend.Application.DTOs.Products;

public sealed class ProductImageDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Alt { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ProductVariantDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }
    public string Height { get; set; } = string.Empty;
    public bool InStock { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class PublicProductDto
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }
    public int Stock { get; set; }
    public string? MainImageUrl { get; set; }
    public string? Attributes { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public decimal? WeightKg { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ProductImageDto> Images { get; set; } = [];
    public List<ProductVariantDto> Variants { get; set; } = [];
}

public sealed class ProductDto
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public Guid SupplierId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal DropPrice { get; set; }
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }
    public int Stock { get; set; }
    public string? MainImageUrl { get; set; }
    public string? Attributes { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public decimal? WeightKg { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<ProductImageDto> Images { get; set; } = [];
    public List<ProductVariantDto> Variants { get; set; } = [];
}

public sealed class CreateProductImageDto
{
    [Required]
    public string Url { get; set; } = string.Empty;

    public string? Alt { get; set; }
    public int SortOrder { get; set; }
}

public class CreateProductDto : IValidatableObject
{
    public Guid CategoryId { get; set; }
    public Guid SupplierId { get; set; }

    [Required(ErrorMessage = "Артикул (SKU) є обов'язковим.")]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "Назва товару є обов'язковою.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Slug товару є обов'язковим.")]
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Закупівельна ціна (DropPrice) не може бути від'ємною.")]
    public decimal DropPrice { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Ціна продажу (Price) повинна бути більшою за 0.")]
    public decimal Price { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "Стара ціна (OldPrice) повинна бути більшою за 0.")]
    public decimal? OldPrice { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Залишок на складі (Stock) не може бути від'ємним.")]
    public int Stock { get; set; }

    public string? MainImageUrl { get; set; }
    public string? Attributes { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }

    [Range(0, 100000, ErrorMessage = "Вага товару (WeightKg) не може бути від'ємною.")]
    public decimal? WeightKg { get; set; }

    public bool IsActive { get; set; } = true;
    public List<CreateProductImageDto>? Images { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DropPrice > Price)
        {
            yield return new ValidationResult(
                "Закупівельна ціна (DropPrice) не може перевищувати роздрібну ціну продажу (Price).",
                [nameof(DropPrice), nameof(Price)]);
        }
    }
}

public sealed class UpdateProductDto : CreateProductDto
{
}

public sealed class UpdateStockDto
{
    [Range(0, int.MaxValue)]
    public int? Stock { get; set; }

    [Range(0, int.MaxValue)]
    public int? StockQuantity { get; set; }

    public int GetStock() => Stock ?? StockQuantity ?? 0;
}

public sealed class UpdateProductStatusDto
{
    public bool IsActive { get; init; }
}

public sealed class PublicProductQueryParameters
{
    public Guid? CategoryId { get; set; }
    public string? Search { get; set; }
    public string? SortBy { get; set; } = "date_desc";
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

public sealed class ProductQueryParameters
{
    public Guid? CategoryId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Search { get; set; }
    public bool? OnlyActive { get; set; }
    public string? SortBy { get; set; } = "date_desc";
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

public sealed class PagedResult<T>
{
    public IEnumerable<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}
