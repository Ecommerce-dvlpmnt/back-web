using EcommerceBackend.Application.DTOs.Analytics;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Application.Services;

public sealed class AnalyticsService(AppDbContext dbContext) : Interfaces.IAnalyticsService
{
    private static readonly OrderStatus[] ExcludedStatuses =
    [
        OrderStatus.Cancelled,
        OrderStatus.Refused,
        OrderStatus.Returned
    ];

    public async Task<AnalyticsSummaryResponse> GetSummaryAsync(string? period, CancellationToken cancellationToken = default)
    {
        var (currentStart, currentEnd, prevStart, prevEnd) = ResolvePeriodDates(period);

        // Fetch orders for current and previous period
        var allOrders = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= prevStart && o.CreatedAt < currentEnd)
            .Select(o => new
            {
                o.CreatedAt,
                o.OrderStatus,
                o.PaymentStatus,
                o.TotalPrice,
                o.TotalProfit
            })
            .ToListAsync(cancellationToken);

        var currentOrders = allOrders.Where(o => o.CreatedAt >= currentStart && o.CreatedAt < currentEnd).ToList();
        var prevOrders = allOrders.Where(o => o.CreatedAt >= prevStart && o.CreatedAt < prevEnd).ToList();

        // Valid orders (exclude cancelled/returned)
        var validCurrent = currentOrders.Where(o => !ExcludedStatuses.Contains(o.OrderStatus)).ToList();
        var validPrev = prevOrders.Where(o => !ExcludedStatuses.Contains(o.OrderStatus)).ToList();

        // 1. Revenue
        var currentRevenue = Math.Round(validCurrent.Sum(o => o.TotalPrice), 2);
        var prevRevenue = Math.Round(validPrev.Sum(o => o.TotalPrice), 2);
        var revenueGrowth = CalculateGrowth(currentRevenue, prevRevenue);

        // 2. Net profit
        var currentProfit = Math.Round(validCurrent.Sum(o => o.TotalProfit), 2);
        var prevProfit = Math.Round(validPrev.Sum(o => o.TotalProfit), 2);
        var profitGrowth = CalculateGrowth(currentProfit, prevProfit);

        // 3. Orders count & average check
        var currentCount = validCurrent.Count;
        var prevCount = validPrev.Count;
        var ordersGrowth = CalculateGrowth(currentCount, prevCount);
        var avgCheck = currentCount > 0 ? Math.Round(currentRevenue / currentCount, 2) : 0m;

        // 4. Conversion / paid rate
        var totalOrders = currentOrders.Count;
        var paidCount = currentOrders.Count(o => o.PaymentStatus == PaymentStatus.Paid || o.OrderStatus == OrderStatus.Delivered);
        var paidRate = totalOrders > 0 ? Math.Round((decimal)paidCount / totalOrders * 100m, 1) : 0m;

        return new AnalyticsSummaryResponse(
            Revenue: new MetricWithGrowthDto(currentRevenue, prevRevenue, revenueGrowth),
            NetProfit: new MetricWithGrowthDto(currentProfit, prevProfit, profitGrowth),
            Orders: new OrdersMetricDto(currentCount, prevCount, ordersGrowth, avgCheck),
            Conversion: new ConversionMetricDto(paidRate));
    }

    public async Task<SalesChartResponse> GetSalesChartAsync(string? period, string? interval, CancellationToken cancellationToken = default)
    {
        var days = (period?.Trim().ToLowerInvariant()) switch
        {
            "today" or "1d" => 1,
            "yesterday" => 2,
            "30d" => 30,
            "season" => 90,
            _ => 7 // default 7d
        };

        var now = DateTime.UtcNow;
        var startDate = now.Date.AddDays(-(days - 1));

        var orders = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.CreatedAt >= startDate && !ExcludedStatuses.Contains(o.OrderStatus))
            .Select(o => new
            {
                o.CreatedAt,
                o.TotalPrice,
                o.TotalProfit
            })
            .ToListAsync(cancellationToken);

        var points = new List<SalesChartPointDto>(days);

        for (var i = 0; i < days; i++)
        {
            var day = startDate.AddDays(i);
            var nextDay = day.AddDays(1);

            var dayOrders = orders.Where(o => o.CreatedAt >= day && o.CreatedAt < nextDay).ToList();

            points.Add(new SalesChartPointDto(
                Date: day.ToString("yyyy-MM-dd"),
                OrdersCount: dayOrders.Count,
                Revenue: Math.Round(dayOrders.Sum(o => o.TotalPrice), 2),
                Profit: Math.Round(dayOrders.Sum(o => o.TotalProfit), 2)));
        }

        return new SalesChartResponse(points);
    }

    public async Task<OrdersBreakdownResponse> GetOrdersBreakdownAsync(CancellationToken cancellationToken = default)
    {
        var orders = await dbContext.Orders
            .AsNoTracking()
            .Select(o => new
            {
                o.OrderStatus,
                o.PaymentMethod,
                o.TotalPrice
            })
            .ToListAsync(cancellationToken);

        var totalOrders = orders.Count;

        // Status breakdown
        var statusOrder = new[]
        {
            OrderStatus.New,
            OrderStatus.Confirmed,
            OrderStatus.Packed,
            OrderStatus.Shipped,
            OrderStatus.Delivered,
            OrderStatus.Cancelled,
            OrderStatus.Refused,
            OrderStatus.Returned
        };

        var byStatus = new List<StatusBreakdownItemDto>();
        foreach (var status in statusOrder)
        {
            var statusOrders = orders.Where(o => o.OrderStatus == status).ToList();
            var count = statusOrders.Count;
            var amount = Math.Round(statusOrders.Sum(o => o.TotalPrice), 2);

            byStatus.Add(new StatusBreakdownItemDto(
                Status: status.ToString().ToLowerInvariant(),
                Label: GetStatusLabel(status),
                Count: count,
                TotalAmount: amount));
        }

        // Payment method breakdown
        var byPaymentMethod = new List<PaymentMethodBreakdownItemDto>();
        foreach (var method in Enum.GetValues<PaymentMethod>())
        {
            var methodOrders = orders.Where(o => o.PaymentMethod == method).ToList();
            var count = methodOrders.Count;
            var percent = totalOrders > 0 ? Math.Round((decimal)count / totalOrders * 100m, 2) : 0m;

            byPaymentMethod.Add(new PaymentMethodBreakdownItemDto(
                Method: method.ToString().ToLowerInvariant(),
                Label: GetPaymentMethodLabel(method),
                Count: count,
                Percent: percent));
        }

        return new OrdersBreakdownResponse(byStatus, byPaymentMethod);
    }

    public async Task<IReadOnlyList<TopProductDto>> GetTopProductsAsync(int limit = 5, CancellationToken cancellationToken = default)
    {
        var take = limit <= 0 ? 5 : (limit > 50 ? 50 : limit);

        var topAggregates = await dbContext.OrderItems
            .AsNoTracking()
            .Where(i => !ExcludedStatuses.Contains(i.Order.OrderStatus))
            .GroupBy(i => new { i.ProductId, i.ProductNameSnapshot })
            .Select(g => new
            {
                ProductId = g.Key.ProductId,
                Name = g.Key.ProductNameSnapshot,
                SalesCount = g.Sum(i => i.Quantity),
                Revenue = g.Sum(i => i.UnitPrice * i.Quantity),
                Profit = g.Sum(i => (i.UnitPrice - i.UnitDropPrice) * i.Quantity)
            })
            .OrderByDescending(x => x.Revenue)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (topAggregates.Count == 0)
        {
            return Array.Empty<TopProductDto>();
        }

        var productIds = topAggregates.Select(x => x.ProductId).Distinct().ToArray();
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        return topAggregates.Select(x =>
        {
            products.TryGetValue(x.ProductId, out var product);
            return new TopProductDto(
                ProductId: x.ProductId,
                Name: product?.Name ?? x.Name,
                Sku: product?.Sku ?? "N/A",
                SalesCount: x.SalesCount,
                Revenue: Math.Round(x.Revenue, 2),
                Profit: Math.Round(x.Profit, 2),
                CurrentStock: product?.Stock ?? 0);
        }).ToList();
    }

    public async Task<IReadOnlyList<LowStockProductDto>> GetLowStockAsync(int threshold = 5, CancellationToken cancellationToken = default)
    {
        var targetThreshold = threshold < 0 ? 5 : threshold;

        var items = await dbContext.Products
            .AsNoTracking()
            .Include(p => p.Supplier)
            .Where(p => p.IsActive && p.Stock <= targetThreshold)
            .OrderBy(p => p.Stock)
            .ThenBy(p => p.Name)
            .Take(50)
            .ToListAsync(cancellationToken);

        return items.Select(p => new LowStockProductDto(
            ProductId: p.Id,
            Name: p.Name,
            Sku: p.Sku,
            Stock: p.Stock,
            SupplierName: p.Supplier?.Name)).ToList();
    }

    private static (DateTime currentStart, DateTime currentEnd, DateTime prevStart, DateTime prevEnd) ResolvePeriodDates(string? period)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;

        return (period?.Trim().ToLowerInvariant()) switch
        {
            "yesterday" => (
                currentStart: today.AddDays(-1),
                currentEnd: today,
                prevStart: today.AddDays(-2),
                prevEnd: today.AddDays(-1)),
            "7d" => (
                currentStart: now.AddDays(-7),
                currentEnd: now,
                prevStart: now.AddDays(-14),
                prevEnd: now.AddDays(-7)),
            "30d" => (
                currentStart: now.AddDays(-30),
                currentEnd: now,
                prevStart: now.AddDays(-60),
                prevEnd: now.AddDays(-30)),
            "season" => (
                currentStart: now.AddDays(-90),
                currentEnd: now,
                prevStart: now.AddDays(-180),
                prevEnd: now.AddDays(-90)),
            _ => ( // "today"
                currentStart: today,
                currentEnd: now,
                prevStart: today.AddDays(-1),
                prevEnd: today)
        };
    }

    private static decimal CalculateGrowth(decimal current, decimal previous)
    {
        if (previous == 0m)
            return current > 0m ? 100m : 0m;

        return Math.Round(((current - previous) / previous) * 100m, 2);
    }

    private static string GetStatusLabel(OrderStatus status) => status switch
    {
        OrderStatus.New => "Нові",
        OrderStatus.Confirmed => "Підтверджено",
        OrderStatus.Packed => "Комплектуються",
        OrderStatus.Shipped => "В дорозі (Нова Пошта)",
        OrderStatus.Delivered => "Успішно забрані",
        OrderStatus.Cancelled => "Скасовано",
        OrderStatus.Refused => "Відмова клієнта",
        OrderStatus.Returned => "Повернення",
        _ => status.ToString()
    };

    private static string GetPaymentMethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.CashOnDelivery => "Накладений платіж",
        PaymentMethod.Online => "Оплата онлайн / IBAN",
        _ => method.ToString()
    };
}
