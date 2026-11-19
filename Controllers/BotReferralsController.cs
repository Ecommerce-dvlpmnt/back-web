using System.Security.Cryptography;
using System.Text;
using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/bot/partners")]
[Produces("application/json")]
public sealed class BotReferralsController(
    IReferralService referralService,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("{telegramUserId:long}")]
    [ProducesResponseType(typeof(BotPartnerProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BotPartnerProfileResponse>> GetProfile(
        long telegramUserId,
        [FromHeader(Name = "X-Bot-Secret")] string? botSecret,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(botSecret))
        {
            return Unauthorized();
        }

        var partner = await referralService.GetBotPartnerProfileAsync(telegramUserId, cancellationToken);
        return partner is null ? NotFound() : Ok(partner);
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(BotPartnerProfileResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BotPartnerProfileResponse>> Register(
        [FromBody] BotRegisterPartnerRequest request,
        [FromHeader(Name = "X-Bot-Secret")] string? botSecret,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(botSecret))
        {
            return Unauthorized();
        }

        var created = await referralService.RegisterBotPartnerAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetProfile), new { telegramUserId = created.TelegramUserId }, created);
    }

    [HttpGet("{telegramUserId:long}/stats")]
    [ProducesResponseType(typeof(BotPartnerStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BotPartnerStatsResponse>> GetStats(
        long telegramUserId,
        [FromHeader(Name = "X-Bot-Secret")] string? botSecret,
        CancellationToken cancellationToken)
    {
        if (!IsAuthorized(botSecret))
        {
            return Unauthorized();
        }

        var stats = await referralService.GetBotPartnerStatsAsync(telegramUserId, cancellationToken);
        return stats is null ? NotFound() : Ok(stats);
    }

    [HttpGet("{telegramUserId:long}/orders")]
    [ProducesResponseType(typeof(BotPartnerOrdersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BotPartnerOrdersResponse>> GetOrders(
        long telegramUserId,
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromHeader(Name = "X-Bot-Secret")] string? botSecret = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(botSecret))
        {
            return Unauthorized();
        }

        var orders = await referralService.GetBotPartnerOrdersAsync(telegramUserId, limit, offset, cancellationToken);
        return orders is null ? NotFound() : Ok(orders);
    }

    private bool IsAuthorized(string? providedSecret)
    {
        var expectedSecret = configuration["Telegram:BotSecret"]
            ?? configuration["TELEGRAM_BOT_SECRET"];

        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            return true;
        }

        if (string.IsNullOrEmpty(providedSecret))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedSecret);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSecret);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
