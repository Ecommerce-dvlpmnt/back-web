namespace EcommerceBackend.Application.Services;

public static class AuditLogActions
{
    public const string OrderStatusChanged = "OrderStatusChanged";
    public const string ProductUpdated = "ProductUpdated";
    public const string ProductStockUpdated = "ProductStockUpdated";
    public const string ProductActivated = "ProductActivated";
    public const string ProductDeactivated = "ProductDeactivated";
    public const string ProductDeleted = "ProductDeleted";
    public const string ReviewStatusChanged = "ReviewStatusChanged";
}

public static class AuditLogEntityTypes
{
    public const string Order = "Order";
    public const string Product = "Product";
    public const string Review = "Review";
}
