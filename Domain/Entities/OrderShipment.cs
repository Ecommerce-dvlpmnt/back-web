using EcommerceBackend.Domain;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Domain.Entities;

public class OrderShipment : ITrackableEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid SupplierId { get; set; }
    public string? SupplierOrderRef { get; set; }
    public string? TrackingNumber { get; set; }
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    public string? AdminComment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Order Order { get; set; } = null!;
    public Supplier Supplier { get; set; } = null!;
}
