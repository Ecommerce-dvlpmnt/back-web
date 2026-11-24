using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    /// <summary>Створення інвойсу в Monobank для існуючого замовлення з деталізованим кошиком для пРРО.</summary>
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
            request?.WebhookUrl,
            cancellationToken);

        return Ok(invoice);
    }

    /// <summary>Створення платежу з переліком товарів, знижками/надбавками та фіскалізацією (пРРО).</summary>
    [HttpPost("monobank/create-fiscal-invoice")]
    [ProducesResponseType(typeof(MonobankInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MonobankInvoiceResponse>> CreateFiscalInvoice(
        [FromBody] CreateFiscalInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var invoice = await monobankService.CreateFiscalInvoiceAsync(request, cancellationToken);
        return Ok(invoice);
    }

    /// <summary>Створення тестового або довільного інвойсу в Monobank (за замовчуванням: 100 грн, "Тестова оплата").</summary>
    [HttpPost("monobank/create-test-invoice")]
    [ProducesResponseType(typeof(MonobankInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MonobankInvoiceResponse>> CreateTestInvoice(
        [FromBody] CreateCustomMonobankInvoiceRequest? request,
        CancellationToken cancellationToken)
    {
        var customRequest = request ?? new CreateCustomMonobankInvoiceRequest();
        var invoice = await monobankService.CreateCustomInvoiceAsync(customRequest, cancellationToken);
        return Ok(invoice);
    }

    /// <summary>Отримання сформованих фіскальних чеків (дані та посилання на tax.gov.ua) за ID інвойсу.</summary>
    [HttpGet("monobank/fiscal-checks/{invoiceId}")]
    [ProducesResponseType(typeof(MonobankFiscalChecksResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MonobankFiscalChecksResponse>> GetFiscalChecks(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var checks = await monobankService.GetFiscalChecksAsync(invoiceId, cancellationToken);
        return Ok(checks);
    }

    /// <summary>Завантаження друкованого PDF-файлу фіскального чека Monobank (пРРО).</summary>
    [HttpGet("monobank/fiscal-checks/{invoiceId}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFiscalCheckPdf(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var pdfBytes = await monobankService.GetFiscalCheckPdfBytesAsync(invoiceId, cancellationToken);
        if (pdfBytes == null || pdfBytes.Length == 0)
        {
            return NotFound(new { error = "PDF фіскального чека ще не сформовано або інвойс не знайдено." });
        }

        return File(pdfBytes, "application/pdf", $"fiscal_check_{invoiceId}.pdf");
    }

    /// <summary>
    /// Офіційний вебхук Monobank з перевіркою ECDSA підпису в заголовку X-Sign.
    /// Доступний за обома маршрутами: /api/payments/monobank/webhook та /api/payment/webhook.
    /// </summary>
    [HttpPost("monobank/webhook")]
    [HttpPost("/api/payment/webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HandleMonobankWebhook(CancellationToken cancellationToken)
    {
        // 1. Зчитуємо сире тіло запиту для перевірки підпису
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        var rawBytes = Encoding.UTF8.GetBytes(rawBody);
        var xSign = Request.Headers["X-Sign"].ToString();

        logger.LogInformation("Отримано вебхук Monobank (довжина тіла: {Length} байт, X-Sign: {XSign})",
            rawBytes.Length, string.IsNullOrWhiteSpace(xSign) ? "<MISSING>" : xSign[..Math.Min(16, xSign.Length)] + "...");

        // 2. Верифікація підпису ECDSA
        var isSignatureValid = await monobankService.VerifyWebhookSignatureAsync(rawBytes, xSign, cancellationToken);
        if (!isSignatureValid)
        {
            logger.LogWarning("Відхилено вебхук Monobank: недійсний або відсутній підпис X-Sign.");
            return Unauthorized("Недійсний підпис X-Sign від Monobank.");
        }

        // 3. Десеріалізація та обробка
        MonobankWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MonobankWebhookPayload>(rawBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Помилка десеріалізації тіла вебхука Monobank: {RawBody}", rawBody);
            return BadRequest("Некоректний формат JSON.");
        }

        if (payload is null)
        {
            logger.LogWarning("Пусте тіло вебхука Monobank.");
            return BadRequest("Пусте тіло вебхука.");
        }

        try
        {
            await monobankService.ProcessWebhookAsync(payload, cancellationToken);
            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Помилка при обробці вебхука Monobank для інвойсу {InvoiceId}", payload.InvoiceId);
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
