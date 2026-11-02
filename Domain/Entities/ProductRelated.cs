namespace EcommerceBackend.Domain.Entities;

public class ProductRelated
{
    public Guid ProductId { get; set; }
    public Guid RelatedProductId { get; set; }

    public Product Product { get; set; } = null!;
    public Product RelatedProduct { get; set; } = null!;
}
