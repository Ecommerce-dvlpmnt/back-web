using EcommerceBackend.Domain;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Domain.Entities;

public class OrderStatusHistory
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public OrderStatus? OldStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public Guid? ChangedByAdminId { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }

    public Order Order { get; set; } = null!;
    public Admin? ChangedByAdmin { get; set; }
}
