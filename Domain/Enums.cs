namespace EcommerceBackend.Domain.Enums;

public enum OrderStatus
{
    New,
    Confirmed,
    Packed,
    Shipped,
    Delivered,
    Cancelled,
    Refused,
    Returned
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Refunded
}

public enum PaymentMethod
{
    CashOnDelivery = 0, // Оплата при отриманні
    Iban = 1,           // Оплата за реквізитами IBAN
    Online = 2          // Оплата карткою онлайн (plata by mono)
}

public enum ShipmentStatus
{
    Pending,
    Requested,
    Confirmed,
    Shipped,
    Problem,
    Cancelled
}

public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public enum ReferralRewardStatus
{
    None,
    Pending,
    Paid,
    Cancelled
}

public enum PayoutRequestStatus
{
    Pending,
    Completed,
    Rejected
}
