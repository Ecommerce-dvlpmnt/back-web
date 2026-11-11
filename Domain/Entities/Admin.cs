namespace EcommerceBackend.Domain.Entities;

public class Admin : ITrackableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public long? TelegramId { get; set; }
    public string Role { get; set; } = "Manager"; // "Admin", "Manager"
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<OrderStatusHistory> StatusChanges { get; set; } = new List<OrderStatusHistory>();
}
