using System.Security.Cryptography;
using System.Text;
using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/payments")]
[Produces("application/json")]
public sealed class PaymentsController(
    IOrderService orderService,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("webhook")]
    [ProducesResponseType(typeof(OrderPaymentConfirmationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderPaymentConfirmationResponse>> HandleWebhook(
        [FromBody] PaymentWebhookRequest request,
        [FromHeader(Name = "X-Webhook-Secret")] string? webhookSecret,
        CancellationToken cancellationToken)
    {
        var expectedSecret = configuration["Payments:WebhookSecret"]
            ?? configuration["PAYMENTS_WEBHOOK_SECRET"];

        if (string.IsNullOrWhiteSpace(expectedSecret) || !SecretsMatch(webhookSecret, expectedSecret))
        {
            return Unauthorized();
        }

        var result = await orderService.ProcessPaymentWebhookAsync(request, cancellationToken);
        return Ok(result);
    }

    private static bool SecretsMatch(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
