using EcommerceBackend.Domain;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Domain.Entities;

public class Order : ITrackableEntity
{
    public Guid Id { get; set; }
    public int OrderNumber { get; set; }
    public string PublicId { get; set; } = null!;
    public string CustomerFirstName { get; set; } = null!;
    public string CustomerLastName { get; set; } = null!;
    public string CustomerPhone { get; set; } = null!;
    public string? CustomerComment { get; set; }
    public string? DeliveryCityRef { get; set; }
    public string? DeliveryCityName { get; set; }
    public string? DeliveryWarehouseRef { get; set; }
    public string? DeliveryWarehouseName { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CashOnDelivery;
    public string? PaymentProvider { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public OrderStatus OrderStatus { get; set; } = OrderStatus.New;
    public string? ExternalPaymentId { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal TotalProfit { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public Guid? CampaignId { get; set; }
    public string? AdminComment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Campaign? Campaign { get; set; }
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderShipment> Shipments { get; set; } = new List<OrderShipment>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
}
