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
    CashOnDelivery,
    Online
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
