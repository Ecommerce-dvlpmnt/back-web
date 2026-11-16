using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Domain.Entities;

public class Review : ITrackableEntity
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid? CategoryId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string? Pros { get; set; }
    public string? Cons { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    public string? AdminComment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Product Product { get; set; } = null!;
    public Category? Category { get; set; }
}
