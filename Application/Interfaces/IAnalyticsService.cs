using EcommerceBackend.Application.DTOs.Analytics;

namespace EcommerceBackend.Application.Interfaces;

public interface IAnalyticsService
{
    Task<AnalyticsSummaryResponse> GetSummaryAsync(string? period, CancellationToken cancellationToken = default);

    Task<SalesChartResponse> GetSalesChartAsync(string? period, string? interval, CancellationToken cancellationToken = default);

    Task<OrdersBreakdownResponse> GetOrdersBreakdownAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TopProductDto>> GetTopProductsAsync(int limit = 5, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LowStockProductDto>> GetLowStockAsync(int threshold = 5, CancellationToken cancellationToken = default);
}
