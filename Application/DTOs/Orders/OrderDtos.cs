using System.ComponentModel.DataAnnotations;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.DTOs.Orders;

/// <summary>Payload for creating an order and reserving its items from stock.</summary>
public sealed record CreateOrderRequest
{
    [Required, StringLength(100)]
    public required string CustomerFirstName { get; init; }

    [Required, StringLength(100)]
    public required string CustomerLastName { get; init; }

    [Required, StringLength(20)]
    public required string CustomerPhone { get; init; }

    public string? CustomerComment { get; init; }
    public string? DeliveryCityRef { get; init; }
    public string? DeliveryCityName { get; init; }
    public string? DeliveryWarehouseRef { get; init; }
    public string? DeliveryWarehouseName { get; init; }
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.CashOnDelivery;

    [Required, MinLength(1)]
    public required IReadOnlyList<CreateOrderItemRequest> Items { get; init; }
}

/// <summary>A product and quantity requested for an order.</summary>
public sealed record CreateOrderItemRequest
{
    [Required]
    public Guid ProductId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}

/// <summary>An order returned by the API.</summary>
public sealed record OrderResponse(
    Guid Id,
    int OrderNumber,
    OrderStatus OrderStatus,
    PaymentMethod PaymentMethod,
    decimal TotalPrice,
    IReadOnlyList<OrderItemResponse> Items);

/// <summary>An order line returned by the API.</summary>
public sealed record OrderItemResponse(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);
