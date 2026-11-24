using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EcommerceBackend.Application.Services;

public sealed class MonobankService(
    HttpClient httpClient,
    IOrderService orderService,
    AppDbContext dbContext,
    IConfiguration configuration,
    ILogger<MonobankService> logger) : IMonobankService
{
    private const string DefaultBaseUrl = "https://api.monobank.ua";
    private static string? _cachedPublicKey;
    private static readonly SemaphoreSlim _pubKeyLock = new(1, 1);

    public async Task<MonobankInvoiceResponse> CreateInvoiceForOrderAsync(
        string orderPublicId,
        string? customRedirectUrl = null,
        string? customWebhookUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderPublicId);

        var order = await orderService.GetByPublicIdAsync(orderPublicId, cancellationToken)
            ?? throw new KeyNotFoundException($"Замовлення '{orderPublicId}' не знайдено.");

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            throw new InvalidOperationException($"Замовлення '{orderPublicId}' вже успішно оплачене.");
        }

        var token = GetToken();

        var redirectUrl = customRedirectUrl
            ?? configuration["Monobank:RedirectUrl"]
            ?? configuration["MONOBANK_REDIRECT_URL"]
            ?? $"{configuration["Referral:PublicSiteUrl"] ?? "https://www.jingledecor.com.ua"}/payment-result";

        var webhookUrl = customWebhookUrl
            ?? configuration["Monobank:WebhookUrl"]
            ?? configuration["MONOBANK_WEBHOOK_URL"]
            ?? "https://mysite.com/api/payment/webhook";

        // Amount in kopecks (e.g. 100 UAH = 10000)
        var amountInKopecks = (long)Math.Round(order.TotalPrice * 100m, MidpointRounding.AwayFromZero);

        var basketItems = order.Items.Select(item => new
        {
            name = item.ProductName,
            qty = item.Quantity,
            sum = (long)Math.Round(item.UnitPrice * item.Quantity * 100m, MidpointRounding.AwayFromZero),
            unit = "шт."
        }).ToList();

        var requestBody = new
        {
            amount = amountInKopecks,
            ccy = 980, // UAH
            merchantPaymInfo = new
            {
                reference = order.PublicId,
                destination = $"Оплата замовлення {order.PublicId}",
                comment = $"Замовлення {order.PublicId} в інтернет-магазині Jingle Decor",
                basketOrder = basketItems
            },
            redirectUrl,
            webHookUrl = string.IsNullOrWhiteSpace(webhookUrl) ? null : webhookUrl,
            validity = 3600 // 1 hour
        };

        var requestUri = $"{GetBaseUrl()}/api/merchant/invoice/create";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri);
        requestMessage.Headers.Add("X-Token", token);
        requestMessage.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        logger.LogInformation("Creating Monobank invoice for order {OrderPublicId}, amount {Amount} kop, webhook: {WebhookUrl}",
            order.PublicId, amountInKopecks, webhookUrl);

        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Monobank invoice creation failed for {OrderPublicId}. Status: {StatusCode}, Body: {Body}",
                order.PublicId, response.StatusCode, responseContent);
            throw new HttpRequestException($"Помилка створення інвойсу в Monobank: {responseContent}");
        }

        var invoiceResult = JsonSerializer.Deserialize<MonobankInvoiceResponse>(responseContent)
            ?? throw new InvalidOperationException("Отримано пусту відповідь від Monobank API.");

        logger.LogInformation("Monobank invoice {InvoiceId} created successfully for order {OrderPublicId}.",
            invoiceResult.InvoiceId, order.PublicId);

        return invoiceResult;
    }

    public async Task<MonobankInvoiceResponse> CreateCustomInvoiceAsync(
        CreateCustomMonobankInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = GetToken();
        var reference = string.IsNullOrWhiteSpace(request.Reference)
            ? $"test_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
            : request.Reference.Trim();

        var requestBody = new
        {
            amount = request.AmountInKopecks,
            ccy = 980, // UAH
            merchantPaymInfo = new
            {
                reference,
                destination = request.Destination,
                comment = request.Destination
            },
            redirectUrl = request.RedirectUrl,
            webHookUrl = request.WebhookUrl,
            validity = 3600
        };

        var requestUri = $"{GetBaseUrl()}/api/merchant/invoice/create";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri);
        requestMessage.Headers.Add("X-Token", token);
        requestMessage.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        logger.LogInformation("Creating custom Monobank invoice: ref={Reference}, amount={Amount} kop, webhook={WebhookUrl}",
            reference, request.AmountInKopecks, request.WebhookUrl);

        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Monobank custom invoice creation failed. Status: {StatusCode}, Body: {Body}",
                response.StatusCode, responseContent);
            throw new HttpRequestException($"Помилка створення інвойсу в Monobank: {responseContent}");
        }

        var invoiceResult = JsonSerializer.Deserialize<MonobankInvoiceResponse>(responseContent)
            ?? throw new InvalidOperationException("Отримано пусту відповідь від Monobank API.");

        logger.LogInformation("Custom Monobank invoice created: invoiceId={InvoiceId}, pageUrl={PageUrl}",
            invoiceResult.InvoiceId, invoiceResult.PageUrl);

        return invoiceResult;
    }

    public async Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBodyBytes,
        string? xSignBase64,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(xSignBase64))
        {
            logger.LogWarning("Monobank webhook rejected: missing X-Sign header.");
            return false;
        }

        // 1. Try with currently cached public key
        var pubKey = await GetPublicKeyAsync(forceRefresh: false, cancellationToken);
        if (!string.IsNullOrWhiteSpace(pubKey))
        {
            if (CheckSignature(rawBodyBytes, xSignBase64, pubKey))
            {
                return true;
            }
        }

        // 2. If verification failed, refresh public key once (handles key rotation)
        logger.LogWarning("Monobank signature verification failed with cached key. Refreshing public key from API...");
        pubKey = await GetPublicKeyAsync(forceRefresh: true, cancellationToken);

        if (!string.IsNullOrWhiteSpace(pubKey) && CheckSignature(rawBodyBytes, xSignBase64, pubKey))
        {
            logger.LogInformation("Monobank signature verification succeeded after public key refresh.");
            return true;
        }

        logger.LogError("Monobank webhook signature verification failed permanently.");
        return false;
    }

    public async Task<OrderPaymentConfirmationResponse?> ProcessWebhookAsync(
        MonobankWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        logger.LogInformation(
            "MONOBANK WEBHOOK RECEIVED => InvoiceId={InvoiceId}, Status={Status}, Amount={Amount} kop, Ref={Reference}, FailureReason={FailureReason}, Modified={ModifiedDate}",
            payload.InvoiceId, payload.Status, payload.Amount, payload.Reference, payload.FailureReason, payload.ModifiedDate);

        if (string.IsNullOrWhiteSpace(payload.Reference))
        {
            logger.LogWarning("Monobank webhook does not contain a Reference (Order Public ID).");
            return null;
        }

        // Find corresponding order in database by PublicId or ExternalPaymentId
        var order = await dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.PublicId == payload.Reference || o.ExternalPaymentId == payload.InvoiceId, cancellationToken);

        var now = DateTime.UtcNow;
        var amountUah = payload.Amount / 100m;
        var logEntry = $"[Monobank {now:yyyy-MM-dd HH:mm:ss}] Invoice={payload.InvoiceId}, Status={payload.Status}, Amount={amountUah:F2} UAH" +
                       (string.IsNullOrWhiteSpace(payload.FailureReason) ? "" : $", Reason={payload.FailureReason}");

        if (order is not null)
        {
            // Persist invoiceId, payment provider, date and detailed payment log on the order
            order.ExternalPaymentId = payload.InvoiceId;
            order.PaymentProvider = "Monobank";
            order.UpdatedAt = now;
            order.AdminComment = string.IsNullOrWhiteSpace(order.AdminComment)
                ? logEntry
                : $"{order.AdminComment}\n{logEntry}";

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Updated order {OrderPublicId} with payment metadata in database.", order.PublicId);
        }
        else
        {
            logger.LogWarning("No order found matching reference '{Reference}' or invoice '{InvoiceId}'. Payment details logged.",
                payload.Reference, payload.InvoiceId);
        }

        // Status handling: success, failure, processing, hold, expired, reversed
        var normalizedStatus = payload.Status.ToLowerInvariant().Trim();
        switch (normalizedStatus)
        {
            case "success":
                logger.LogInformation("Processing successful payment for order {Reference}.", payload.Reference);
                var webhookRequest = new PaymentWebhookRequest
                {
                    OrderPublicId = payload.Reference,
                    ExternalPaymentId = payload.InvoiceId,
                    PaymentProvider = "Monobank",
                    PaymentStatus = PaymentStatus.Paid
                };
                return await orderService.ProcessPaymentWebhookAsync(webhookRequest, cancellationToken);

            case "failure":
            case "expired":
                logger.LogWarning("Monobank payment failed/expired for order {Reference}: {Reason}",
                    payload.Reference, payload.FailureReason ?? payload.Status);
                if (order is not null && order.PaymentStatus != PaymentStatus.Paid)
                {
                    order.PaymentStatus = PaymentStatus.Failed;
                    order.UpdatedAt = now;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                break;

            case "reversed":
                logger.LogInformation("Processing refund/reversal for order {Reference}.", payload.Reference);
                var refundRequest = new PaymentWebhookRequest
                {
                    OrderPublicId = payload.Reference,
                    ExternalPaymentId = payload.InvoiceId,
                    PaymentProvider = "Monobank",
                    PaymentStatus = PaymentStatus.Refunded
                };
                return await orderService.ProcessPaymentWebhookAsync(refundRequest, cancellationToken);

            case "processing":
            case "hold":
            case "created":
                logger.LogInformation("Monobank payment for order {Reference} is currently in state: {Status}",
                    payload.Reference, normalizedStatus);
                if (order is not null && order.PaymentStatus != PaymentStatus.Paid)
                {
                    order.PaymentStatus = PaymentStatus.Pending;
                    order.UpdatedAt = now;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                break;

            default:
                logger.LogWarning("Unrecognized Monobank status '{Status}' for order {Reference}.", payload.Status, payload.Reference);
                break;
        }

        return null;
    }

    public async Task<MonobankInvoiceStatusResponse> GetInvoiceStatusAsync(
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);

        var token = GetToken();
        var requestUri = $"{GetBaseUrl()}/api/merchant/invoice/status?invoiceId={Uri.EscapeDataString(invoiceId)}";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        requestMessage.Headers.Add("X-Token", token);

        logger.LogInformation("Checking Monobank status for invoice {InvoiceId}", invoiceId);

        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Monobank status check failed for invoice {InvoiceId}. Status: {StatusCode}, Body: {Body}",
                invoiceId, response.StatusCode, responseContent);
            throw new HttpRequestException($"Помилка перевірки статусу в Monobank: {responseContent}");
        }

        return JsonSerializer.Deserialize<MonobankInvoiceStatusResponse>(responseContent)
            ?? throw new InvalidOperationException("Отримано пусту відповідь від Monobank API.");
    }

    private async Task<string> GetPublicKeyAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && !string.IsNullOrWhiteSpace(_cachedPublicKey))
        {
            return _cachedPublicKey;
        }

        await _pubKeyLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && !string.IsNullOrWhiteSpace(_cachedPublicKey))
            {
                return _cachedPublicKey;
            }

            var token = GetToken();
            var requestUri = $"{GetBaseUrl()}/api/merchant/pubkey";
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
            requestMessage.Headers.Add("X-Token", token);

            logger.LogInformation("Fetching Monobank public key from {Uri}", requestUri);

            var response = await httpClient.SendAsync(requestMessage, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Failed to fetch Monobank pubkey. Status: {StatusCode}, Body: {Body}",
                    response.StatusCode, responseContent);
                throw new HttpRequestException($"Не вдалося отримати публічний ключ Monobank: {responseContent}");
            }

            var pubKeyResponse = JsonSerializer.Deserialize<MonobankPubKeyResponse>(responseContent)
                ?? throw new InvalidOperationException("Отримано пусту відповідь від ендпоінту публічного ключа Monobank.");

            _cachedPublicKey = pubKeyResponse.Key;
            logger.LogInformation("Monobank public key successfully cached.");
            return _cachedPublicKey;
        }
        finally
        {
            _pubKeyLock.Release();
        }
    }

    private static bool CheckSignature(byte[] bodyBytes, string xSignBase64, string keyString)
    {
        try
        {
            var signatureBytes = Convert.FromBase64String(xSignBase64.Trim());
            using var ecdsa = ECDsa.Create();

            var candidate = keyString.Trim();
            if (!candidate.Contains("BEGIN PUBLIC KEY"))
            {
                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(candidate));
                    if (decoded.Contains("BEGIN PUBLIC KEY"))
                    {
                        candidate = decoded;
                    }
                }
                catch
                {
                    // keep original
                }
            }

            if (candidate.Contains("BEGIN PUBLIC KEY"))
            {
                ecdsa.ImportFromPem(candidate);
            }
            else
            {
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(candidate), out _);
            }

            // 1. Try standard RFC 3279 DER Sequence format (standard for Java SHA256withECDSA)
            if (ecdsa.VerifyData(bodyBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            {
                return true;
            }

            // 2. Fallback to IEEE P1363 fixed field concatenation (raw r || s)
            if (ecdsa.VerifyData(bodyBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private string GetToken()
    {
        var token = configuration["Monobank:Token"]
            ?? configuration["MONOBANK_TOKEN"]
            ?? configuration["MONOBANK__TOKEN"];

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Monobank API Token не налаштовано в конфігурації (Monobank:Token або MONOBANK_TOKEN).");
        }

        return token.Trim();
    }

    private string GetBaseUrl()
    {
        return configuration["Monobank:BaseUrl"]?.TrimEnd('/') ?? DefaultBaseUrl;
    }
}
