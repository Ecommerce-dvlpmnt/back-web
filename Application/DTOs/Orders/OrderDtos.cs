using System.ComponentModel.DataAnnotations;
using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.DTOs.Orders;

/// <summary>Payload for creating an order and reserving its items from stock.</summary>
public sealed record CreateOrderRequest
{
    // First and last name
    [Required(ErrorMessage = "Вкажіть ім'я покупця.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Ім'я покупця має містити від 2 до 100 символів.")]
    public string? CustomerFirstName { get; init; }

    [StringLength(100, ErrorMessage = "Прізвище покупця не може перевищувати 100 символів.")]
    public string? CustomerLastName { get; init; }

    [Required(ErrorMessage = "Вкажіть номер телефону.")]
    [StringLength(30, ErrorMessage = "Номер телефону не може перевищувати 30 символів.")]
    public required string CustomerPhone { get; init; }

    [EmailAddress, StringLength(150)]
    public string? CustomerEmail { get; init; }

    public string? CustomerComment { get; init; }

    // Delivery fields (Nova Poshta branch / postomat / address)
    public string? DeliveryType { get; init; } // "Warehouse", "Postomat", "Branch", "Поштомат", "Відділення"
    public string? DeliveryCityRef { get; init; }

    [Required(ErrorMessage = "Вкажіть місто доставки.")]
    [StringLength(150, ErrorMessage = "Назва міста не може перевищувати 150 символів.")]
    public string? DeliveryCityName { get; init; }

    public string? DeliveryWarehouseRef { get; init; }

    [Required(ErrorMessage = "Вкажіть відділення або поштомат Нової Пошти.")]
    [StringLength(250, ErrorMessage = "Назва відділення не може перевищувати 250 символів.")]
    public string? DeliveryWarehouseName { get; init; }

    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.CashOnDelivery;

    [StringLength(32)]
    public string? ReferralCode { get; init; }

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
    IReadOnlyList<OrderItemResponse> Items,
    Guid? ReferrerId = null);

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
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

/// <summary>Details of an order item for admin view (including drop price and profit).</summary>
public sealed record AdminOrderItemResponse(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal UnitDropPrice,
    decimal LineTotal,
    decimal LineProfit);

/// <summary>Status change log entry.</summary>
public sealed record OrderStatusHistoryDto(
    Guid Id,
    OrderStatus? OldStatus,
    OrderStatus NewStatus,
    Guid? ChangedByAdminId,
    string? ChangedByAdminName,
    string? Comment,
    DateTime CreatedAt);

/// <summary>Full order details for the admin view.</summary>
public sealed record AdminOrderDetailResponse(
    Guid Id,
    int OrderNumber,
    string PublicId,
    string CustomerFirstName,
    string CustomerLastName,
    string CustomerFullName,
    string CustomerPhone,
    string? CustomerEmail,
    string? CustomerComment,
    string? DeliveryCityRef,
    string? DeliveryCityName,
    string? DeliveryWarehouseRef,
    string? DeliveryWarehouseName,
    OrderStatus OrderStatus,
    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus,
    decimal TotalPrice,
    decimal TotalProfit,
    string? AdminComment,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<AdminOrderItemResponse> Items,
    IReadOnlyList<OrderStatusHistoryDto> StatusHistory,
    Guid? ReferrerId = null,
    string? ReferrerName = null,
    string? ReferralCode = null,
    decimal ReferralRewardAmount = 0m,
    ReferralRewardStatus ReferralRewardStatus = ReferralRewardStatus.None);

/// <summary>Request to update the admin internal comment for an order.</summary>
public sealed record UpdateOrderCommentRequest
{
    public string? Comment { get; init; }
    public string? AdminComment { get; init; }

    public string? GetComment() => !string.IsNullOrWhiteSpace(AdminComment) ? AdminComment : Comment;
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

public sealed record ConfirmOrderPaymentRequest
{
    [StringLength(20)]
    public string? PaymentProvider { get; init; }

    [StringLength(100)]
    public string? ExternalPaymentId { get; init; }

    public string? Comment { get; init; }
}

public sealed record PaymentWebhookRequest
{
    public Guid? OrderId { get; init; }

    [StringLength(32)]
    public string? OrderPublicId { get; init; }

    [StringLength(100)]
    public string? ExternalPaymentId { get; init; }

    [StringLength(20)]
    public string? PaymentProvider { get; init; }

    [Required]
    public PaymentStatus PaymentStatus { get; init; } = PaymentStatus.Paid;
}

public sealed record OrderPaymentConfirmationResponse(
    OrderResponse Order,
    ReferralRewardResult? ReferralReward,
    bool TelegramNotificationSent);
