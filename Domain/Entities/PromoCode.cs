namespace EcommerceBackend.Domain.Entities;

public class PromoCode
{
    public Guid Id { get; set; }
    public string Code { get; set; } = null!;
    public decimal DiscountPercent { get; set; }
    public int? MaxUses { get; set; }
    public int UsedCount { get; set; }
    public bool IsActive { get; set; } = true;
    public long? CreatedByTelegramUserId { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
