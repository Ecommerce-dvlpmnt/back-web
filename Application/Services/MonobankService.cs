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

        var order = await dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.PublicId == orderPublicId, cancellationToken)
            ?? throw new KeyNotFoundException($"Замовлення '{orderPublicId}' не знайдено.");

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            throw new InvalidOperationException($"Замовлення '{orderPublicId}' вже успішно оплачене.");
        }

        var redirectUrl = customRedirectUrl
            ?? configuration["Monobank:RedirectUrl"]
            ?? configuration["MONOBANK_REDIRECT_URL"]
            ?? $"{configuration["Referral:PublicSiteUrl"] ?? "https://www.jingledecor.com.ua"}/payment-result";

        var webhookUrl = customWebhookUrl
            ?? configuration["Monobank:WebhookUrl"]
            ?? configuration["MONOBANK_WEBHOOK_URL"]
            ?? "https://mysite.com/api/payment/webhook";

        // Map items to fiscal format
        var fiscalItems = order.Items.Select(item =>
        {
            var unitSum = (long)Math.Round(item.UnitPrice * 100m, MidpointRounding.AwayFromZero);
            var itemTotal = (long)Math.Round(item.UnitPrice * item.Quantity * 100m, MidpointRounding.AwayFromZero);
            return new MonobankFiscalItemDto
            {
                Name = item.ProductNameSnapshot,
                Qty = item.Quantity,
                Sum = unitSum,
                Total = itemTotal,
                Code = item.ProductId.ToString("N")[..8].ToUpperInvariant(),
                Tax = [0], // Без ПДВ за замовчуванням (або [1] для 20%)
                Unit = "шт."
            };
        }).ToList();

        List<MonobankDiscountDto>? discounts = null;
        if (order.DiscountAmount > 0)
        {
            discounts =
            [
                new MonobankDiscountDto
                {
                    Type = "discount",
                    Mode = "value",
                    Value = (long)Math.Round(order.DiscountAmount * 100m, MidpointRounding.AwayFromZero)
                }
            ];
        }

        var fiscalRequest = new CreateFiscalInvoiceRequest
        {
            BasketOrder = fiscalItems,
            CartDiscounts = discounts,
            Destination = $"Оплата замовлення {order.PublicId}",
            Reference = order.PublicId,
            RedirectUrl = redirectUrl,
            WebhookUrl = webhookUrl
        };

        return await CreateFiscalInvoiceAsync(fiscalRequest, cancellationToken);
    }

    public async Task<MonobankInvoiceResponse> CreateFiscalInvoiceAsync(
        CreateFiscalInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BasketOrder == null || request.BasketOrder.Count == 0)
        {
            throw new ArgumentException("Кошик повинен містити хоча б один товар.", nameof(request));
        }

        var token = GetToken();

        // 1. Обробка товарів та перевірка/розрахунок сум кожного товару
        long subtotal = 0;
        var formattedBasket = new List<object>();

        foreach (var item in request.BasketOrder)
        {
            // Якщо total не задано, обчислюємо: round(qty * sum)
            var lineTotal = item.Total > 0
                ? item.Total
                : (long)Math.Round(item.Qty * item.Sum, MidpointRounding.AwayFromZero);

            // Обробка знижок на рівні конкретного товару
            long itemFinalTotal = lineTotal;
            List<object>? itemDiscountsFormatted = null;

            if (item.Discounts != null && item.Discounts.Count > 0)
            {
                itemDiscountsFormatted = [];
                foreach (var d in item.Discounts)
                {
                    var isDiscount = d.Type.Equals("discount", StringComparison.OrdinalIgnoreCase);
                    var isPercent = d.Mode.Equals("percent", StringComparison.OrdinalIgnoreCase);

                    long diff = isPercent
                        ? (long)Math.Round(lineTotal * (d.Value / 100m), MidpointRounding.AwayFromZero)
                        : (long)d.Value;

                    itemFinalTotal = isDiscount ? (itemFinalTotal - diff) : (itemFinalTotal + diff);

                    itemDiscountsFormatted.Add(new
                    {
                        type = d.Type.ToLowerInvariant(),
                        mode = d.Mode.ToLowerInvariant(),
                        value = d.Value
                    });
                }
            }

            subtotal += itemFinalTotal;

            var itemObj = new Dictionary<string, object?>
            {
                ["name"] = item.Name,
                ["qty"] = item.Qty,
                ["sum"] = item.Sum,
                ["total"] = lineTotal,
                ["code"] = item.Code ?? "ITEM",
                ["tax"] = item.Tax ?? [0],
                ["unit"] = item.Unit ?? "шт."
            };

            if (!string.IsNullOrWhiteSpace(item.Barcode))
            {
                itemObj["barcode"] = item.Barcode.Trim();
            }

            if (itemDiscountsFormatted != null && itemDiscountsFormatted.Count > 0)
            {
                itemObj["discounts"] = itemDiscountsFormatted;
            }

            formattedBasket.Add(itemObj);
        }

        // 2. Обробка знижок або надбавок на рівні всього кошика
        long finalInvoiceAmount = subtotal;
        List<object>? cartDiscountsFormatted = null;

        if (request.CartDiscounts != null && request.CartDiscounts.Count > 0)
        {
            cartDiscountsFormatted = [];
            foreach (var d in request.CartDiscounts)
            {
                var isDiscount = d.Type.Equals("discount", StringComparison.OrdinalIgnoreCase);
                var isPercent = d.Mode.Equals("percent", StringComparison.OrdinalIgnoreCase);

                long diff = isPercent
                    ? (long)Math.Round(subtotal * (d.Value / 100m), MidpointRounding.AwayFromZero)
                    : (long)d.Value;

                finalInvoiceAmount = isDiscount ? (finalInvoiceAmount - diff) : (finalInvoiceAmount + diff);

                cartDiscountsFormatted.Add(new
                {
                    type = d.Type.ToLowerInvariant(),
                    mode = d.Mode.ToLowerInvariant(),
                    value = d.Value
                });
            }
        }

        // Страховка від від'ємної суми
        if (finalInvoiceAmount <= 0)
        {
            finalInvoiceAmount = 100; // мінімум 1 грн (100 коп)
        }

        var reference = string.IsNullOrWhiteSpace(request.Reference)
            ? $"fiscal_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
            : request.Reference.Trim();

        var merchantPaymInfo = new Dictionary<string, object?>
        {
            ["reference"] = reference,
            ["destination"] = request.Destination,
            ["comment"] = request.Destination,
            ["basketOrder"] = formattedBasket
        };

        if (cartDiscountsFormatted != null && cartDiscountsFormatted.Count > 0)
        {
            merchantPaymInfo["discounts"] = cartDiscountsFormatted;
        }

        var payload = new
        {
            amount = finalInvoiceAmount,
            ccy = 980, // UAH
            merchantPaymInfo,
            redirectUrl = request.RedirectUrl,
            webHookUrl = request.WebhookUrl,
            validity = 3600
        };

        var requestUri = $"{GetBaseUrl()}/api/merchant/invoice/create";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUri);
        requestMessage.Headers.Add("X-Token", token);
        requestMessage.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        logger.LogInformation(
            "Creating Monobank fiscal invoice: ref={Reference}, itemsCount={ItemsCount}, amount={Amount} kop, subtotal={Subtotal} kop",
            reference, formattedBasket.Count, finalInvoiceAmount, subtotal);

        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Monobank fiscal invoice creation failed. Status: {StatusCode}, Body: {Body}",
                response.StatusCode, responseContent);
            throw new HttpRequestException($"Помилка створення фіскального інвойсу в Monobank: {responseContent}");
        }

        var invoiceResult = JsonSerializer.Deserialize<MonobankInvoiceResponse>(responseContent)
            ?? throw new InvalidOperationException("Отримано пусту відповідь від Monobank API.");

        logger.LogInformation("Monobank fiscal invoice created successfully: invoiceId={InvoiceId}, pageUrl={PageUrl}",
            invoiceResult.InvoiceId, invoiceResult.PageUrl);

        return invoiceResult;
    }

    public async Task<MonobankInvoiceResponse> CreateCustomInvoiceAsync(
        CreateCustomMonobankInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var defaultItem = new MonobankFiscalItemDto
        {
            Name = request.Destination,
            Qty = 1m,
            Sum = request.AmountInKopecks,
            Total = request.AmountInKopecks,
            Code = "CUSTOM",
            Tax = [0],
            Unit = "шт."
        };

        var fiscalRequest = new CreateFiscalInvoiceRequest
        {
            BasketOrder = [defaultItem],
            Destination = request.Destination,
            Reference = request.Reference,
            RedirectUrl = request.RedirectUrl,
            WebhookUrl = request.WebhookUrl
        };

        return await CreateFiscalInvoiceAsync(fiscalRequest, cancellationToken);
    }

    public async Task<MonobankFiscalChecksResponse> GetFiscalChecksAsync(
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);

        var token = GetToken();
        var requestUri = $"{GetBaseUrl()}/api/merchant/invoice/fiscal-checks?invoiceId={Uri.EscapeDataString(invoiceId)}";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        requestMessage.Headers.Add("X-Token", token);

        logger.LogInformation("Fetching fiscal checks for invoice {InvoiceId}", invoiceId);

        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Monobank fiscal checks retrieval failed for invoice {InvoiceId}. Status: {StatusCode}, Body: {Body}",
                invoiceId, response.StatusCode, responseContent);
            throw new HttpRequestException($"Помилка отримання фіскальних чеків з Monobank: {responseContent}");
        }

        return JsonSerializer.Deserialize<MonobankFiscalChecksResponse>(responseContent)
            ?? throw new InvalidOperationException("Отримано пусту відповідь від сервісу фіскальних чеків.");
    }

    public async Task<byte[]?> GetFiscalCheckPdfBytesAsync(
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        var checksResponse = await GetFiscalChecksAsync(invoiceId, cancellationToken);
        if (checksResponse.Checks == null || checksResponse.Checks.Count == 0)
        {
            logger.LogWarning("No fiscal checks found for invoice {InvoiceId}", invoiceId);
            return null;
        }

        // Шукаємо чек операції продажу (sale)
        var saleCheck = checksResponse.Checks.FirstOrDefault(c => c.Type == "sale" && !string.IsNullOrWhiteSpace(c.FileBase64))
            ?? checksResponse.Checks.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.FileBase64));

        if (saleCheck == null || string.IsNullOrWhiteSpace(saleCheck.FileBase64))
        {
            logger.LogWarning("Fiscal check for invoice {InvoiceId} does not yet have an attached PDF file (status: {Status})",
                invoiceId, checksResponse.Checks[0].Status);
            return null;
        }

        try
        {
            return Convert.FromBase64String(saleCheck.FileBase64.Trim());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to decode Base64 fiscal PDF for invoice {InvoiceId}", invoiceId);
            return null;
        }
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

        var pubKey = await GetPublicKeyAsync(forceRefresh: false, cancellationToken);
        if (!string.IsNullOrWhiteSpace(pubKey))
        {
            if (CheckSignature(rawBodyBytes, xSignBase64, pubKey))
            {
                return true;
            }
        }

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

        var order = await dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.PublicId == payload.Reference || o.ExternalPaymentId == payload.InvoiceId, cancellationToken);

        var now = DateTime.UtcNow;
        var amountUah = payload.Amount / 100m;
        var logEntry = $"[Monobank {now:yyyy-MM-dd HH:mm:ss}] Invoice={payload.InvoiceId}, Status={payload.Status}, Amount={amountUah:F2} UAH" +
                       (string.IsNullOrWhiteSpace(payload.FailureReason) ? "" : $", Reason={payload.FailureReason}");

        if (order is not null)
        {
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

            if (ecdsa.VerifyData(bodyBytes, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            {
                return true;
            }

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
