using System.Security.Claims;
using EcommerceBackend.Application.DTOs.Finance;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers.Admin;

[ApiController]
[Route("api/admin/finance")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class AdminFinanceController(
    IFinanceService financeService,
    ICurrencyRateService currencyRateService) : ControllerBase
{
    /// <summary>Отримати поточний офіційний курс долара (НБУ) для перерахунку та захисту від інфляції.</summary>
    [HttpGet("exchange-rate")]
    [ProducesResponseType(typeof(CurrencyRateInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CurrencyRateInfo>> GetExchangeRate(CancellationToken ct)
    {
        var rate = await currencyRateService.GetCurrentRatesAsync(ct);
        return Ok(rate);
    }

    /// <summary>Зведення по партнерським часткам, інвестиціям та розподілу чистого прибутку (грн та USD).</summary>
    [HttpGet("partners-summary")]
    [ProducesResponseType(typeof(PartnersSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PartnersSummaryResponse>> GetPartnersSummary(CancellationToken ct)
    {
        var summary = await financeService.GetPartnersSummaryAsync(ct);
        return Ok(summary);
    }

    /// <summary>Фіксація витрат на маркетинг (Google Ads, Meta Ads, TikTok тощо) у грн або USD.</summary>
    [HttpPost("ad-spend")]
    [ProducesResponseType(typeof(AdSpendResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdSpendResponse>> AddAdSpend(
        [FromBody] CreateAdSpendRequest request,
        CancellationToken ct)
    {
        Guid? adminId = null;
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(claim, out var parsed))
        {
            adminId = parsed;
        }

        var response = await financeService.AddAdSpendAsync(request, adminId, ct);
        return CreatedAtAction(nameof(GetAdSpends), new { id = response.Id }, response);
    }

    /// <summary>Історія всіх списань та витрат на рекламу.</summary>
    [HttpGet("ad-spend")]
    [ProducesResponseType(typeof(IReadOnlyList<AdSpendResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AdSpendResponse>>> GetAdSpends(CancellationToken ct)
    {
        var items = await financeService.GetAdSpendsAsync(ct);
        return Ok(items);
    }

    /// <summary>Видалити запис витрат на рекламу за ID.</summary>
    [HttpDelete("ad-spend/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAdSpend(Guid id, CancellationToken ct)
    {
        var deleted = await financeService.DeleteAdSpendAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Внесення грошового депозиту партнером у рекламний фонд (грн або USD з фіксацією курсу).</summary>
    [HttpPost("deposits")]
    [ProducesResponseType(typeof(PartnerDepositResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PartnerDepositResponse>> AddDeposit(
        [FromBody] CreateDepositRequest request,
        CancellationToken ct)
    {
        Guid? currentAdminId = null;
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(claim, out var parsed))
        {
            currentAdminId = parsed;
        }

        var response = await financeService.AddDepositAsync(request, currentAdminId, ct);
        return CreatedAtAction(nameof(GetDeposits), new { id = response.Id }, response);
    }

    /// <summary>Історія всіх внесених депозитів партнерів з фіксацією курсу долара.</summary>
    [HttpGet("deposits")]
    [ProducesResponseType(typeof(IReadOnlyList<PartnerDepositResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PartnerDepositResponse>>> GetDeposits(CancellationToken ct)
    {
        var items = await financeService.GetDepositsAsync(ct);
        return Ok(items);
    }

    /// <summary>Оновити роль або фіксований відсоток партнера.</summary>
    [HttpPatch("partners/{adminId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePartnerSettings(
        Guid adminId,
        [FromBody] UpdatePartnerSettingsRequest request,
        CancellationToken ct)
    {
        var updated = await financeService.UpdatePartnerSettingsAsync(adminId, request, ct);
        return updated ? NoContent() : NotFound();
    }
}
