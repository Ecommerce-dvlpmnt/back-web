using System.Security.Cryptography;
using System.Text;
using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/payments")]
[Produces("application/json")]
public sealed class PaymentsController(
    IOrderService orderService,
    IMonobankService monobankService,
    IConfiguration configuration,
    ILogger<PaymentsController> logger) : ControllerBase
{
    /// <summary>Створення інвойсу в Monobank для існуючого замовлення за його публічним номером (наприклад JD-ABC12345).</summary>
    [HttpPost("monobank/create/{orderPublicId}")]
    [ProducesResponseType(typeof(MonobankInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MonobankInvoiceResponse>> CreateMonobankInvoice(
        string orderPublicId,
        [FromBody] CreateMonobankPaymentRequest? request,
        CancellationToken cancellationToken)
    {
        var invoice = await monobankService.CreateInvoiceForOrderAsync(
            orderPublicId,
            request?.RedirectUrl,
            cancellationToken);

        return Ok(invoice);
    }

    /// <summary>Офіційний вебхук від Monobank після зміни статусу оплати (успіх, скасування тощо).</summary>
    [HttpPost("monobank/webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> HandleMonobankWebhook(
        [FromBody] MonobankWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Отримано вебхук Monobank для інвойсу {InvoiceId}, статус: {Status}",
                payload.InvoiceId, payload.Status);

            await monobankService.ProcessWebhookAsync(payload, cancellationToken);
            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Помилка при обробці вебхука Monobank для інвойсу {InvoiceId}", payload.InvoiceId);
            // Завжди повертаємо 200 для банку, щоб він не спамив повторними запитами при бізнес-помилках
            return Ok();
        }
    }

    /// <summary>Перевірка живого статусу інвойсу за його ID напряму з Monobank API.</summary>
    [HttpGet("monobank/status/{invoiceId}")]
    [ProducesResponseType(typeof(MonobankInvoiceStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MonobankInvoiceStatusResponse>> GetMonobankStatus(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var status = await monobankService.GetInvoiceStatusAsync(invoiceId, cancellationToken);
        return Ok(status);
    }

    /// <summary>Стандартний захищений вебхук для внутрішніх/інших платіжних провайдерів.</summary>
    [HttpPost("webhook")]
    [ProducesResponseType(typeof(OrderPaymentConfirmationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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
