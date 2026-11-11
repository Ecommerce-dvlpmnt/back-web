using System.ComponentModel.DataAnnotations;

namespace EcommerceBackend.Application.DTOs.Products;

public sealed class ProductImageDto
{
    public Guid Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Alt { get; set; }
    public int SortOrder { get; set; }
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
}

public sealed class CreateProductImageDto
{
    [Required]
    public string Url { get; set; } = string.Empty;

    public string? Alt { get; set; }
    public int SortOrder { get; set; }
}

public class CreateProductDto
{
    public Guid CategoryId { get; set; }
    public Guid SupplierId { get; set; }

    [Required]
    public string Sku { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }
    public decimal DropPrice { get; set; }

    [Range(0.01, double.MaxValue)]
    public decimal Price { get; set; }

    public decimal? OldPrice { get; set; }

    [Range(0, int.MaxValue)]
    public int Stock { get; set; }

    public string? MainImageUrl { get; set; }
    public string? Attributes { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public decimal? WeightKg { get; set; }
    public bool IsActive { get; set; } = true;
    public List<CreateProductImageDto>? Images { get; set; }
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
