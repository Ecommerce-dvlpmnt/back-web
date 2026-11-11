namespace EcommerceBackend.Domain.Entities;

public class Admin : ITrackableEntity
{
    public Guid Id { get; set; }
    public string Username { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = null!;
    public long? TelegramId { get; set; }
    public string Role { get; set; } = "Admin"; // "Admin", "Manager"
    public string? PartnerRole { get; set; } // "Lead / Dev", "Dev / Backend", "Marketing", "Operations"
    public decimal? FixedSharePercent { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<OrderStatusHistory> StatusChanges { get; set; } = new List<OrderStatusHistory>();
    public ICollection<PartnerDeposit> Deposits { get; set; } = new List<PartnerDeposit>();
}
