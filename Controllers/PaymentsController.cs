using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    /// <summary>
    /// Створення інвойсу в Monobank для існуючого замовлення суворо за цінами та знижками з БД.
    /// Не приймає з клієнта суму, позиції чи URL-адреси.
    /// </summary>
    [HttpPost("monobank/create/{orderPublicId}")]
    [EnableRateLimiting("order-limit")]
    [ProducesResponseType(typeof(MonobankInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MonobankInvoiceResponse>> CreateMonobankInvoice(
        string orderPublicId,
        CancellationToken cancellationToken)
    {
        var invoice = await monobankService.CreateInvoiceForOrderAsync(orderPublicId, cancellationToken);
        return Ok(invoice);
    }

    /// <summary>
    /// Безпечна перевірка статусу оплати замовлення за його публічним кодом (для сторінки /payment-result на фронтенді).
    /// Лише читання з БД без розкриття персональних даних (PII) та без зміни статусу оплати.
    /// </summary>
    [HttpGet("monobank/order-status/{orderPublicId}")]
    [EnableRateLimiting("order-limit")]
    [ProducesResponseType(typeof(MonobankOrderPaymentStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MonobankOrderPaymentStatusResponse>> GetOrderPaymentStatus(
        string orderPublicId,
        CancellationToken cancellationToken)
    {
        var status = await monobankService.GetOrderPaymentStatusAsync(orderPublicId, cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>Адмін-ендпоінт: створення тестового/ручного платежу з фіскальним кошиком (пРРО).</summary>
    [HttpPost("monobank/create-fiscal-invoice")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(MonobankInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MonobankInvoiceResponse>> CreateFiscalInvoice(
        [FromBody] CreateFiscalInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var invoice = await monobankService.CreateFiscalInvoiceAsync(request, cancellationToken);
        return Ok(invoice);
    }

    /// <summary>Адмін-ендпоінт: створення тестового інвойсу в Monobank (за замовчуванням 100 грн).</summary>
    [HttpPost("monobank/create-test-invoice")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(MonobankInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MonobankInvoiceResponse>> CreateTestInvoice(
        [FromBody] CreateCustomMonobankInvoiceRequest? request,
        CancellationToken cancellationToken)
    {
        var customRequest = request ?? new CreateCustomMonobankInvoiceRequest();
        var invoice = await monobankService.CreateCustomInvoiceAsync(customRequest, cancellationToken);
        return Ok(invoice);
    }

    /// <summary>Отримання сформованих фіскальних чеків (дані та посилання на tax.gov.ua) за ID інвойсу замовлення.</summary>
    [HttpGet("monobank/fiscal-checks/{invoiceId}")]
    [EnableRateLimiting("order-limit")]
    [ProducesResponseType(typeof(MonobankFiscalChecksResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MonobankFiscalChecksResponse>> GetFiscalChecks(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var isAdmin = User?.IsInRole("Admin") == true;
        var checks = await monobankService.GetFiscalChecksAsync(invoiceId, requireOrderMatch: !isAdmin, cancellationToken);
        return Ok(checks);
    }

    /// <summary>Завантаження друкованого PDF-файлу фіскального чека Monobank (пРРО).</summary>
    [HttpGet("monobank/fiscal-checks/{invoiceId}/pdf")]
    [EnableRateLimiting("order-limit")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DownloadFiscalCheckPdf(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var isAdmin = User?.IsInRole("Admin") == true;
        var pdfBytes = await monobankService.GetFiscalCheckPdfBytesAsync(invoiceId, requireOrderMatch: !isAdmin, cancellationToken);
        if (pdfBytes == null || pdfBytes.Length == 0)
        {
            return NotFound(new { error = "PDF фіскального чека ще не сформовано або інвойс не знайдено." });
        }

        var safeInvoiceId = new string(invoiceId.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(64).ToArray());
        if (string.IsNullOrEmpty(safeInvoiceId))
        {
            safeInvoiceId = "receipt";
        }

        return File(pdfBytes, "application/pdf", $"fiscal_check_{safeInvoiceId}.pdf");
    }

    /// <summary>
    /// Офіційний вебхук Monobank з перевіркою ECDSA підпису в заголовку X-Sign по сирим байтам тіла.
    /// Доступний за обома маршрутами: /api/payments/monobank/webhook та /api/payment/webhook.
    /// </summary>
    [HttpPost("monobank/webhook")]
    [HttpPost("/api/payment/webhook")]
    [RequestSizeLimit(65_536)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HandleMonobankWebhook(CancellationToken cancellationToken)
    {
        // 1. Зчитуємо точні сирі байти тіла запиту напряму з потоку без перекодування символів
        await using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, cancellationToken);
        var rawBytes = ms.ToArray();

        var xSign = Request.Headers["X-Sign"].ToString();

        logger.LogInformation("Отримано вебхук Monobank (довжина тіла: {Length} байт, X-Sign: {XSign})",
            rawBytes.Length, string.IsNullOrWhiteSpace(xSign) ? "<MISSING>" : xSign[..Math.Min(16, xSign.Length)] + "...");

        // 2. Верифікація ECDSA підпису (SHA-256)
        var isSignatureValid = await monobankService.VerifyWebhookSignatureAsync(rawBytes, xSign, cancellationToken);
        if (!isSignatureValid)
        {
            logger.LogWarning("Відхилено вебхук Monobank: недійсний або відсутній підпис X-Sign.");
            return Unauthorized("Недійсний підпис X-Sign від Monobank.");
        }

        // 3. Десеріалізація перевірених байтів
        MonobankWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MonobankWebhookPayload>(rawBytes, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Помилка десеріалізації тіла вебхука Monobank.");
            return BadRequest("Некоректний формат JSON.");
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.InvoiceId) || string.IsNullOrWhiteSpace(payload.Status))
        {
            logger.LogWarning("Неповне або пусте тіло вебхука Monobank.");
            return BadRequest("Неповне тіло вебхука.");
        }

        try
        {
            await monobankService.ProcessWebhookAsync(payload, cancellationToken);
            return Ok();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            // Детермінована бізнес-відмова (скасоване замовлення тощо) — повертаємо 200, щоб Monobank не спамив ретраями.
            // Тимчасові помилки БД/мережі сюди не потрапляють -> прокидаються як 500, щоб Monobank повторив доставку!
            logger.LogWarning(ex, "Бізнес-відмова при обробці вебхука Monobank для інвойсу {InvoiceId}", payload.InvoiceId);
            return Ok();
        }
    }

    /// <summary>Перевірка живого статусу інвойсу за його ID (для існуючих замовлень або адміністраторів).</summary>
    [HttpGet("monobank/status/{invoiceId}")]
    [EnableRateLimiting("order-limit")]
    [ProducesResponseType(typeof(MonobankInvoiceStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MonobankInvoiceStatusResponse>> GetMonobankStatus(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var isAdmin = User?.IsInRole("Admin") == true;
        var status = await monobankService.GetInvoiceStatusAsync(invoiceId, requireOrderMatch: !isAdmin, cancellationToken);
        return Ok(status);
    }

    /// <summary>Стандартний захищений вебхук для внутрішніх/інших платіжних провайдерів.</summary>
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
