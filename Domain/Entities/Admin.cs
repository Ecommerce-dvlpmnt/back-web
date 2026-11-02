namespace EcommerceBackend.Domain.Entities;

public class Admin
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public long? TelegramId { get; set; }
    public string Role { get; set; } = "Manager";

    public ICollection<OrderStatusHistory> StatusChanges { get; set; } = new List<OrderStatusHistory>();
}
