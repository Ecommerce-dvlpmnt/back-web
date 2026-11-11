namespace EcommerceBackend.Application.DTOs.Analytics;

public sealed record MetricWithGrowthDto(
    decimal Total,
    decimal PreviousPeriod,
    decimal GrowthPercent);

public sealed record OrdersMetricDto(
    int Count,
    int PreviousPeriod,
    decimal GrowthPercent,
    decimal AverageCheck);

public sealed record ConversionMetricDto(
    decimal PaidRate);

public sealed record AnalyticsSummaryResponse(
    MetricWithGrowthDto Revenue,
    MetricWithGrowthDto NetProfit,
    OrdersMetricDto Orders,
    ConversionMetricDto Conversion);

public sealed record SalesChartPointDto(
    string Date,
    int OrdersCount,
    decimal Revenue,
    decimal Profit);

public sealed record SalesChartResponse(
    IReadOnlyList<SalesChartPointDto> Points);

public sealed record StatusBreakdownItemDto(
    string Status,
    string Label,
    int Count,
    decimal TotalAmount);

public sealed record PaymentMethodBreakdownItemDto(
    string Method,
    string Label,
    int Count,
    decimal Percent);

public sealed record OrdersBreakdownResponse(
    IReadOnlyList<StatusBreakdownItemDto> ByStatus,
    IReadOnlyList<PaymentMethodBreakdownItemDto> ByPaymentMethod);

public sealed record TopProductDto(
    Guid ProductId,
    string Name,
    string Sku,
    int SalesCount,
    decimal Revenue,
    decimal Profit,
    int CurrentStock);

public sealed record LowStockProductDto(
    Guid ProductId,
    string Name,
    string Sku,
    int Stock,
    string? SupplierName);
