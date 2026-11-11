using EcommerceBackend.Application.DTOs.Analytics;
using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers.Admin;

[ApiController]
[Route("api/admin/analytics")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class AdminAnalyticsController(IAnalyticsService analyticsService) : ControllerBase
{
    /// <summary>Зведені показники (Summary KPI) для дашборду. Параметр period: today, yesterday, 7d, 30d, season.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AnalyticsSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AnalyticsSummaryResponse>> GetSummary(
        [FromQuery] string? period,
        CancellationToken ct)
    {
        var summary = await analyticsService.GetSummaryAsync(period, ct);
        return Ok(summary);
    }

    /// <summary>Графік динаміки виторгу, прибутку та замовлень по днях.</summary>
    [HttpGet("charts/sales")]
    [ProducesResponseType(typeof(SalesChartResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SalesChartResponse>> GetSalesChart(
        [FromQuery] string? period,
        [FromQuery] string? interval,
        CancellationToken ct)
    {
        var chart = await analyticsService.GetSalesChartAsync(period, interval, ct);
        return Ok(chart);
    }

    /// <summary>Воронка замовлень за статусами та типами оплат.</summary>
    [HttpGet("orders-breakdown")]
    [ProducesResponseType(typeof(OrdersBreakdownResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OrdersBreakdownResponse>> GetOrdersBreakdown(CancellationToken ct)
    {
        var breakdown = await analyticsService.GetOrdersBreakdownAsync(ct);
        return Ok(breakdown);
    }

    /// <summary>Топ найуспішніших товарів за обсягом продажів та виручкою.</summary>
    [HttpGet("top-products")]
    [ProducesResponseType(typeof(IReadOnlyList<TopProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<TopProductDto>>> GetTopProducts(
        [FromQuery] int limit = 5,
        CancellationToken ct = default)
    {
        var products = await analyticsService.GetTopProductsAsync(limit, ct);
        return Ok(products);
    }

    /// <summary>Алерти критичних залишків на складі (товари, де залишок менше або дорівнює threshold).</summary>
    [HttpGet("low-stock")]
    [ProducesResponseType(typeof(IReadOnlyList<LowStockProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<LowStockProductDto>>> GetLowStock(
        [FromQuery] int threshold = 5,
        CancellationToken ct = default)
    {
        var products = await analyticsService.GetLowStockAsync(threshold, ct);
        return Ok(products);
    }
}
