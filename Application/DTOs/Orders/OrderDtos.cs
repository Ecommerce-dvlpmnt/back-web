using System.ComponentModel.DataAnnotations;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.DTOs.Orders;

/// <summary>Payload for creating an order and reserving its items from stock.</summary>
public sealed record CreateOrderRequest
{
    // First/Last name or single full name / customer name
    public string? CustomerFirstName { get; init; }
    public string? CustomerLastName { get; init; }
    public string? CustomerName { get; init; }
    public string? FullName { get; init; }
    public string? Name { get; init; }

    [Required, StringLength(30)]
    public required string CustomerPhone { get; init; }
    public string? Phone { get; init; }

    [EmailAddress, StringLength(150)]
    public string? CustomerEmail { get; init; }
    public string? Email { get; init; }

    public string? CustomerComment { get; init; }
    public string? Comment { get; init; }

    // Delivery fields (Nova Poshta branch / postomat / address)
    public string? DeliveryType { get; init; } // "Warehouse", "Postomat", "Branch", "Поштомат", "Відділення"
    public string? DeliveryCityRef { get; init; }
    public string? DeliveryCityName { get; init; }
    public string? City { get; init; }
    public string? DeliveryWarehouseRef { get; init; }
    public string? DeliveryWarehouseName { get; init; }
    public string? Warehouse { get; init; }
    public string? Address { get; init; }

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
    public int Quantity { get; init; } = 1;
}

/// <summary>An order returned by the API.</summary>
public sealed record OrderResponse(
    Guid Id,
    int OrderNumber,
    string PublicId,
    string CustomerFirstName,
    string CustomerLastName,
    string CustomerFullName,
    string CustomerPhone,
    string? CustomerEmail,
    string? CustomerComment,
    string? DeliveryCityName,
    string? DeliveryWarehouseName,
    OrderStatus OrderStatus,
    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus,
    decimal TotalPrice,
    DateTime CreatedAt,
    IReadOnlyList<OrderItemResponse> Items);

/// <summary>An order line returned by the API.</summary>
public sealed record OrderItemResponse(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>Query parameters for listing orders with filtering and pagination.</summary>
public sealed record OrderListQuery
{
    public OrderStatus? Status { get; init; }
    public string? Search { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>Paginated list of orders.</summary>
public sealed record PagedOrderListResponse(
    IReadOnlyList<OrderResponse> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

/// <summary>Payload for updating the status of an order.</summary>
public sealed record UpdateOrderStatusRequest
{
    [Required]
    public OrderStatus Status { get; init; }

    public string? Comment { get; init; }
}
