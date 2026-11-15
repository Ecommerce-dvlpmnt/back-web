using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.Services;

public static class OrderCancellationPolicy
{
    public static OrderCancellationDecision Check(
        OrderStatus status,
        PaymentStatus paymentStatus)
    {
        if (status == OrderStatus.Cancelled)
            return OrderCancellationDecision.AlreadyCancelled;

        if (status != OrderStatus.New)
            return OrderCancellationDecision.NotNewStatus;

        if (paymentStatus == PaymentStatus.Paid)
            return OrderCancellationDecision.Paid;

        return OrderCancellationDecision.CanCancel;
    }
}

public enum OrderCancellationDecision
{
    CanCancel,
    AlreadyCancelled,
    NotNewStatus,
    Paid
}
