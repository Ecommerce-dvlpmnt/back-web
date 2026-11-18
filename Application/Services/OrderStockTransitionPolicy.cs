using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.Services;

public static class OrderStockTransitionPolicy
{
    public static bool IsStockReleased(OrderStatus status) =>
        status is OrderStatus.Cancelled or OrderStatus.Refused or OrderStatus.Returned;

    public static bool RequiresStockReturn(OrderStatus oldStatus, OrderStatus newStatus) =>
        !IsStockReleased(oldStatus) && IsStockReleased(newStatus);

    public static bool RequiresStockDeduction(OrderStatus oldStatus, OrderStatus newStatus) =>
        IsStockReleased(oldStatus) && !IsStockReleased(newStatus);
}
