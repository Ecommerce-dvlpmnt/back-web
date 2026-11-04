using EcommerceBackend.Domain;

namespace EcommerceBackend.Domain.Entities;

public class Product : ITrackableEntity
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public Guid SupplierId { get; set; }
    public string Sku { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public decimal DropPrice { get; set; }
    public decimal Price { get; set; }
    public decimal? OldPrice { get; set; }
    public int Stock { get; set; }
    public DateTime? StockUpdatedAt { get; set; }
    public string? MainImageUrl { get; set; }
    public string? Attributes { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public decimal? WeightKg { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
    public ICollection<ProductRelated> RelatedLinks { get; set; } = new List<ProductRelated>();
    public ICollection<CampaignProduct> CampaignLinks { get; set; } = new List<CampaignProduct>();
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
}
