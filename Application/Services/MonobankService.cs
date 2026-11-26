using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Telegram;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EcommerceBackend.Application.Services;

public sealed class MonobankService(
    HttpClient httpClient,
    IOrderService orderService,
    AppDbContext dbContext,
    IConfiguration configuration,
    ILogger<MonobankService> logger,
    IHttpContextAccessor? httpContextAccessor = null) : IMonobankService
{
    private const string DefaultBaseUrl = "https://api.monobank.ua";
    private const string DefaultPublicSiteUrl = "https://www.jingledecor.com.ua";
    private static readonly TimeSpan PubKeyRefreshCooldown = TimeSpan.FromMinutes(5);

    private static string? _cachedPublicKey;
    private static DateTime _lastPubKeyFetchUtc = DateTime.MinValue;
    private static readonly SemaphoreSlim _pubKeyLock = new(1, 1);

    public async Task<MonobankInvoiceResponse> CreateInvoiceForOrderAsync(
        string orderPublicId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderPublicId);

        var order = await dbContext.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.PublicId == orderPublicId, cancellationToken)
            ?? throw new KeyNotFoundException($"Замовлення '{orderPublicId}' не знайдено.");

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            throw new InvalidOperationException($"Замовлення '{orderPublicId}' вже успішно оплачене.");
        }

        if (order.OrderStatus is OrderStatus.Cancelled or OrderStatus.Refused or OrderStatus.Returned)
        {
            throw new InvalidOperationException($"Не можна створити оплату для скасованого замовлення '{orderPublicId}'.");
        }

        var redirectUrl = BuildOrderRedirectUrl(order.PublicId);
        var webhookUrl = ResolveConfiguredWebhookUrl();
        var defaultTaxCode = configuration.GetValue<int>("Monobank:TaxCode", 0);

        var fiscalItems = order.Items.Select(item =>
        {
            var unitSum = checked((long)Math.Round(item.UnitPrice * 100m, MidpointRounding.AwayFromZero));
            var itemTotal = checked(unitSum * item.Quantity);
            var sku = !string.IsNullOrWhiteSpace(item.Product?.Sku)
                ? item.Product.Sku.Trim()
                : item.ProductId.ToString("N")[..8].ToUpperInvariant();

            return new MonobankFiscalItemDto
            {
                Name = item.ProductNameSnapshot,
                Qty = item.Quantity,
                Sum = unitSum,
                Total = itemTotal,
                Code = sku,
                Tax = [defaultTaxCode],
                Unit = "шт.",
                Icon = ResolveProductIconUrl(item.Product)
            };
        }).ToList();

        var subtotalKopecks = fiscalItems.Sum(i => i.Total);
        var expectedOrderKopecks = checked((long)Math.Round(order.TotalPrice * 100m, MidpointRounding.AwayFromZero));
        var discountDiffKopecks = subtotalKopecks - expectedOrderKopecks;

        List<MonobankDiscountDto>? discounts = null;
        if (discountDiffKopecks > 0)
        {
            discounts =
            [
                new MonobankDiscountDto
                {
                    Type = "discount",
                    Mode = "value",
                    Value = discountDiffKopecks
                }
            ];
        }
        else if (discountDiffKopecks < 0)
        {
            discounts =
            [
                new MonobankDiscountDto
                {
                    Type = "extra",
                    Mode = "value",
                    Value = Math.Abs(discountDiffKopecks)
                }
            ];
        }

        var fiscalRequest = new CreateFiscalInvoiceRequest
        {
            BasketOrder = fiscalItems,
            CartDiscounts = discounts,
            Destination = $"Оплата замовлення #{order.OrderNumber}",
            Reference = order.PublicId,
            RedirectUrl = redirectUrl,
            WebhookUrl = webhookUrl
        };

        var invoiceResult = await CreateFiscalInvoiceCoreAsync(fiscalRequest, isInternalOrderCall: true, cancellationToken);

        // Прив'язуємо створений invoiceId до замовлення одразу після відповіді банку
        order.ExternalPaymentId = invoiceResult.InvoiceId;
        order.PaymentProvider = "Monobank";
        order.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return invoiceResult;
    }

    public Task<MonobankInvoiceResponse> CreateFiscalInvoiceAsync(
        CreateFiscalInvoiceRequest request,
        CancellationToken cancellationToken = default) =>
        CreateFiscalInvoiceCoreAsync(request, isInternalOrderCall: false, cancellationToken);

    private async Task<MonobankInvoiceResponse> CreateFiscalInvoiceCoreAsync(
        CreateFiscalInvoiceRequest request,
        bool isInternalOrderCall,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.BasketOrder == null || request.BasketOrder.Count == 0)
        {
            throw new ArgumentException("Кошик повинен містити хоча б один товар.", nameof(request));
        }

        var reference = string.IsNullOrWhiteSpace(request.Reference)
            ? $"fiscal_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
            : request.Reference.Trim();

        if (!isInternalOrderCall)
        {
            var collidesWithOrder = await dbContext.Orders
                .AsNoTracking()
                .AnyAsync(o => o.PublicId == reference, cancellationToken);

            if (collidesWithOrder)
            {
                throw new InvalidOperationException(
                    "Для оплати існуючого замовлення використовуйте метод CreateInvoiceForOrderAsync.");
            }
        }

        var token = GetToken();
        var defaultTaxCode = configuration.GetValue<int>("Monobank:TaxCode", 0);

        // 1. Сувора перевірка та розрахунок сум позицій кошика до копійки
        long subtotal = 0;
        var formattedBasket = new List<object>(request.BasketOrder.Count);

        foreach (var item in request.BasketOrder)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException("Назва товару в чеку не може бути порожньою.", nameof(request));

            if (item.Qty <= 0m || item.Qty > 100_000m)
                throw new ArgumentException($"Некоректна кількість для позиції '{item.Name}'.", nameof(request));

            if (item.Sum <= 0 || item.Sum > 100_000_000L)
                throw new ArgumentException($"Некоректна ціна для позиції '{item.Name}'.", nameof(request));

            var lineTotal = checked((long)Math.Round(item.Qty * item.Sum, MidpointRounding.AwayFromZero));
            if (item.Total > 0 && item.Total != lineTotal)
            {
                throw new ArgumentException(
                    $"Поле total ({item.Total}) для товару '{item.Name}' не збігається з qty * sum ({lineTotal}).",
                    nameof(request));
            }

            long itemFinalTotal = lineTotal;
            List<object>? itemDiscountsFormatted = null;

            if (item.Discounts != null && item.Discounts.Count > 0)
            {
                itemDiscountsFormatted = [];
                foreach (var d in item.Discounts)
                {
                    var (normType, normMode, diff) = CalculateDiscountAdjustment(lineTotal, d);
                    itemFinalTotal = normType == "discount"
                        ? checked(itemFinalTotal - diff)
                        : checked(itemFinalTotal + diff);

                    itemDiscountsFormatted.Add(new
                    {
                        type = normType,
                        mode = normMode,
                        value = d.Value
                    });
                }

                if (itemFinalTotal <= 0)
                {
                    throw new InvalidOperationException(
                        $"Сума позиції '{item.Name}' після застосування знижки повинна бути більшою за 0.");
                }
            }

            subtotal = checked(subtotal + itemFinalTotal);

            var itemObj = new Dictionary<string, object?>
            {
                ["name"] = item.Name.Trim(),
                ["qty"] = item.Qty,
                ["sum"] = item.Sum,
                ["total"] = lineTotal,
                ["code"] = string.IsNullOrWhiteSpace(item.Code) ? "ITEM" : item.Code.Trim(),
                ["tax"] = item.Tax is { Count: > 0 } ? item.Tax : new[] { defaultTaxCode },
                ["unit"] = string.IsNullOrWhiteSpace(item.Unit) ? "шт." : item.Unit.Trim()
            };

            if (!string.IsNullOrWhiteSpace(item.Barcode))
            {
                itemObj["barcode"] = item.Barcode.Trim();
            }

            if (!string.IsNullOrWhiteSpace(item.Icon)
                && Uri.TryCreate(item.Icon.Trim(), UriKind.Absolute, out var iconUri)
                && (iconUri.Scheme == Uri.UriSchemeHttps || iconUri.Scheme == Uri.UriSchemeHttp))
            {
                itemObj["icon"] = iconUri.ToString();
            }

            if (itemDiscountsFormatted is { Count: > 0 })
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
                var (normType, normMode, diff) = CalculateDiscountAdjustment(subtotal, d);
                finalInvoiceAmount = normType == "discount"
                    ? checked(finalInvoiceAmount - diff)
                    : checked(finalInvoiceAmount + diff);

                cartDiscountsFormatted.Add(new
                {
                    type = normType,
                    mode = normMode,
                    value = d.Value
                });
            }
        }

        if (finalInvoiceAmount <= 0)
        {
            throw new InvalidOperationException("Підсумкова сума інвойсу після знижок повинна бути більшою за 0 копійок.");
        }

        var redirectUrl = !string.IsNullOrWhiteSpace(request.RedirectUrl) && !ContainsPlaceholderDomain(request.RedirectUrl)
            ? request.RedirectUrl.Trim()
            : ResolveConfiguredRedirectUrl();

        var webhookUrl = !string.IsNullOrWhiteSpace(request.WebhookUrl) && !ContainsPlaceholderDomain(request.WebhookUrl)
            ? request.WebhookUrl.Trim()
            : ResolveConfiguredWebhookUrl();

        var merchantPaymInfo = new Dictionary<string, object?>
        {
            ["reference"] = reference,
            ["destination"] = request.Destination,
            ["comment"] = request.Destination,
            ["basketOrder"] = formattedBasket
        };

        if (cartDiscountsFormatted is { Count: > 0 })
        {
            merchantPaymInfo["discounts"] = cartDiscountsFormatted;
        }

        var payload = new Dictionary<string, object?>
        {
            ["amount"] = finalInvoiceAmount,
            ["ccy"] = 980, // UAH
            ["merchantPaymInfo"] = merchantPaymInfo,
            ["redirectUrl"] = redirectUrl,
            ["validity"] = 3600
        };

        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            payload["webHookUrl"] = webhookUrl;
        }

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

        if (request.AmountInKopecks <= 0 || request.AmountInKopecks > 100_000_000L)
        {
            throw new ArgumentException("Сума платежу повинна бути більшою за 0.", nameof(request));
        }

        var defaultTaxCode = configuration.GetValue<int>("Monobank:TaxCode", 0);
        var defaultItem = new MonobankFiscalItemDto
        {
            Name = request.Destination,
            Qty = 1m,
            Sum = request.AmountInKopecks,
            Total = request.AmountInKopecks,
            Code = "CUSTOM",
            Tax = [defaultTaxCode],
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

        return await CreateFiscalInvoiceCoreAsync(fiscalRequest, isInternalOrderCall: false, cancellationToken);
    }

    public async Task<MonobankOrderPaymentStatusResponse?> GetOrderPaymentStatusAsync(
        string orderPublicId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderPublicId))
            return null;

        var normalizedId = orderPublicId.Trim();
        var order = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.PublicId == normalizedId)
            .Select(o => new
            {
                o.PublicId,
                o.OrderNumber,
                o.TotalPrice,
                o.PaymentStatus,
                o.OrderStatus,
                o.ExternalPaymentId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
            return null;

        // Резервна жива синхронізація: якщо клієнт повернувся на /payment-result швидше за вебхук
        // (або вебхук не був доставлений), перевіряємо статус напряму в Monobank API по X-Token.
        if (order.PaymentStatus == PaymentStatus.Pending && !string.IsNullOrWhiteSpace(order.ExternalPaymentId))
        {
            try
            {
                var liveStatus = await GetInvoiceStatusAsync(order.ExternalPaymentId, requireOrderMatch: false, cancellationToken);
                var normLiveStatus = liveStatus.Status?.Trim().ToLowerInvariant();
                if (normLiveStatus is "success" or "failure" or "expired" or "reversed")
                {
                    await ProcessWebhookAsync(
                        new MonobankWebhookPayload
                        {
                            InvoiceId = liveStatus.InvoiceId,
                            Status = liveStatus.Status!,
                            Amount = liveStatus.Amount,
                            FinalAmount = liveStatus.FinalAmount,
                            Ccy = liveStatus.Ccy,
                            Reference = string.IsNullOrWhiteSpace(liveStatus.Reference) ? order.PublicId : liveStatus.Reference,
                            FailureReason = liveStatus.FailureReason,
                            ModifiedDate = liveStatus.ModifiedDate
                        },
                        cancellationToken);

                    var refreshedOrder = await dbContext.Orders
                        .AsNoTracking()
                        .Where(o => o.PublicId == normalizedId)
                        .Select(o => new
                        {
                            o.PublicId,
                            o.OrderNumber,
                            o.TotalPrice,
                            o.PaymentStatus,
                            o.OrderStatus,
                            o.ExternalPaymentId
                        })
                        .FirstOrDefaultAsync(cancellationToken);

                    if (refreshedOrder is null)
                        return null;

                    order = refreshedOrder;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Live Monobank status sync skipped for order {OrderPublicId}", normalizedId);
            }
        }

        var isPaid = order.PaymentStatus == PaymentStatus.Paid;
        var canRetry = !isPaid && order.OrderStatus is not (OrderStatus.Cancelled or OrderStatus.Refused or OrderStatus.Returned);
        var receiptAvailable = isPaid && !string.IsNullOrWhiteSpace(order.ExternalPaymentId);

        return new MonobankOrderPaymentStatusResponse(
            OrderPublicId: order.PublicId,
            OrderNumber: order.OrderNumber,
            TotalPrice: order.TotalPrice,
            PaymentStatus: order.PaymentStatus,
            OrderStatus: order.OrderStatus,
            InvoiceId: order.ExternalPaymentId,
            IsPaid: isPaid,
            CanRetryPayment: canRetry,
            ReceiptAvailable: receiptAvailable);
    }

    public async Task<MonobankFiscalChecksResponse> GetFiscalChecksAsync(
        string invoiceId,
        bool requireOrderMatch = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);
        var cleanId = invoiceId.Trim();

        if (requireOrderMatch)
        {
            await EnsureInvoiceBelongsToOrderAsync(cleanId, cancellationToken);
        }

        var token = GetToken();
        var requestUri = $"{GetBaseUrl()}/api/merchant/invoice/fiscal-checks?invoiceId={Uri.EscapeDataString(cleanId)}";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        requestMessage.Headers.Add("X-Token", token);

        logger.LogInformation("Fetching fiscal checks for invoice {InvoiceId}", cleanId);

        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Monobank fiscal checks retrieval failed for invoice {InvoiceId}. Status: {StatusCode}, Body: {Body}",
                cleanId, response.StatusCode, responseContent);
            throw new HttpRequestException($"Помилка отримання фіскальних чеків з Monobank: {responseContent}");
        }

        return JsonSerializer.Deserialize<MonobankFiscalChecksResponse>(responseContent)
            ?? throw new InvalidOperationException("Отримано пусту відповідь від сервісу фіскальних чеків.");
    }

    public async Task<byte[]?> GetFiscalCheckPdfBytesAsync(
        string invoiceId,
        bool requireOrderMatch = true,
        CancellationToken cancellationToken = default)
    {
        var checksResponse = await GetFiscalChecksAsync(invoiceId, requireOrderMatch, cancellationToken);
        if (checksResponse.Checks == null || checksResponse.Checks.Count == 0)
        {
            logger.LogWarning("No fiscal checks found for invoice {InvoiceId}", invoiceId);
            return null;
        }

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
        if (rawBodyBytes is null || rawBodyBytes.Length == 0 || string.IsNullOrWhiteSpace(xSignBase64))
        {
            logger.LogWarning("Monobank webhook rejected: empty body or missing X-Sign header.");
            return false;
        }

        var pubKey = await GetPublicKeyAsync(forceRefresh: false, cancellationToken);
        if (!string.IsNullOrWhiteSpace(pubKey) && CheckSignature(rawBodyBytes, xSignBase64, pubKey))
        {
            return true;
        }

        // Захист від спаму запитами /api/merchant/pubkey при флуді підробленими X-Sign:
        // оновлюємо публічний ключ не частіше 1 разу на 5 хвилин
        if (DateTime.UtcNow - _lastPubKeyFetchUtc >= PubKeyRefreshCooldown)
        {
            logger.LogWarning("Monobank signature verification failed with cached key. Refreshing public key from API...");
            pubKey = await GetPublicKeyAsync(forceRefresh: true, cancellationToken);

            if (!string.IsNullOrWhiteSpace(pubKey) && CheckSignature(rawBodyBytes, xSignBase64, pubKey))
            {
                logger.LogInformation("Monobank signature verification succeeded after public key refresh.");
                return true;
            }
        }

        logger.LogError("Monobank webhook signature verification failed.");
        return false;
    }

    public async Task<OrderPaymentConfirmationResponse?> ProcessWebhookAsync(
        MonobankWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        logger.LogInformation(
            "MONOBANK WEBHOOK RECEIVED => InvoiceId={InvoiceId}, Status={Status}, Amount={Amount} kop, FinalAmount={FinalAmount}, Ccy={Ccy}, Ref={Reference}, FailureReason={FailureReason}, Modified={ModifiedDate}",
            payload.InvoiceId, payload.Status, payload.Amount, payload.FinalAmount, payload.Ccy, payload.Reference, payload.FailureReason, payload.ModifiedDate);

        var reference = payload.Reference?.Trim();
        var invoiceId = payload.InvoiceId.Trim();

        if (string.IsNullOrWhiteSpace(reference) && string.IsNullOrWhiteSpace(invoiceId))
        {
            logger.LogWarning("Monobank webhook missing both Reference and InvoiceId.");
            return null;
        }

        // Snapshot без трекінгу, щоб не конфліктувати з транзакцією FOR UPDATE в OrderService
        var orderSnapshot = await dbContext.Orders
            .AsNoTracking()
            .Where(o => (reference != null && o.PublicId == reference) || o.ExternalPaymentId == invoiceId)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.PublicId,
                o.TotalPrice,
                o.PaymentStatus,
                o.ExternalPaymentId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (orderSnapshot is null)
        {
            logger.LogWarning("No order found matching reference '{Reference}' or invoice '{InvoiceId}'.",
                reference, invoiceId);
            return null;
        }

        var normalizedStatus = payload.Status.ToLowerInvariant().Trim();

        // 1. Ідемпотентність: якщо замовлення вже оплачене і це не повернення коштів — нічого не перезаписуємо
        if (orderSnapshot.PaymentStatus == PaymentStatus.Paid && normalizedStatus != "reversed")
        {
            logger.LogInformation(
                "Order {OrderPublicId} is already Paid (ExternalPaymentId={ExistingInvoice}). Skipping webhook {InvoiceId} ({Status}).",
                orderSnapshot.PublicId, orderSnapshot.ExternalPaymentId, invoiceId, normalizedStatus);
            return null;
        }

        // 2. Захист від застарілих інвойсів: якщо клієнт створив новий інвойс, а старий протух ("expired"/"failure"), ігноруємо старий
        if (!string.IsNullOrWhiteSpace(orderSnapshot.ExternalPaymentId)
            && !string.Equals(orderSnapshot.ExternalPaymentId, invoiceId, StringComparison.Ordinal)
            && normalizedStatus is not ("success" or "reversed"))
        {
            logger.LogInformation(
                "Ignoring stale Monobank webhook {InvoiceId} ({Status}) for order {OrderPublicId} (active invoice is {ActiveInvoice}).",
                invoiceId, normalizedStatus, orderSnapshot.PublicId, orderSnapshot.ExternalPaymentId);
            return null;
        }

        var amountUah = (payload.FinalAmount ?? payload.Amount) / 100m;
        var auditComment = string.Format(
            CultureInfo.InvariantCulture,
            "[Monobank] Invoice={0}, Status={1}, Amount={2:F2} UAH{3}",
            invoiceId,
            normalizedStatus,
            amountUah,
            string.IsNullOrWhiteSpace(payload.FailureReason) ? "" : $", Reason={payload.FailureReason.Trim()}");

        switch (normalizedStatus)
        {
            case "success":
            {
                var expectedKopecks = checked((long)Math.Round(orderSnapshot.TotalPrice * 100m, MidpointRounding.AwayFromZero));
                var paidKopecks = payload.FinalAmount ?? payload.Amount;

                // Сувора перевірка валюти (980 = UAH) та суми до копійки проти підміни ціни
                if (payload.Ccy != 980 || payload.Amount < expectedKopecks || paidKopecks < expectedKopecks)
                {
                    logger.LogError(
                        "SECURITY ALERT: Monobank amount/currency mismatch for order {OrderPublicId} (invoice {InvoiceId})! Expected {Expected} kop (ccy=980), got amount={Amount}, finalAmount={FinalAmount}, ccy={Ccy}. Order NOT marked Paid.",
                        orderSnapshot.PublicId, invoiceId, expectedKopecks, payload.Amount, payload.FinalAmount, payload.Ccy);

                    await AppendOrderPaymentAuditUnderLockAsync(
                        orderSnapshot.Id,
                        invoiceId,
                        newStatus: null,
                        $"[Monobank SECURITY ALERT] Mismatch on Invoice={invoiceId}: expected {expectedKopecks} kop (UAH), got {paidKopecks} kop (ccy={payload.Ccy})",
                        cancellationToken);
                    return null;
                }

                logger.LogInformation("Confirming verified Monobank payment for order {OrderPublicId} ({Amount} kop).",
                    orderSnapshot.PublicId, paidKopecks);

                var webhookRequest = new PaymentWebhookRequest
                {
                    OrderId = orderSnapshot.Id,
                    ExternalPaymentId = invoiceId,
                    PaymentProvider = "Monobank",
                    PaymentStatus = PaymentStatus.Paid,
                    Comment = auditComment
                };

                var confirmation = await orderService.ProcessPaymentWebhookAsync(webhookRequest, cancellationToken);
                return confirmation;
            }

            case "reversed":
            {
                logger.LogInformation("Processing refund/reversal for order {OrderPublicId}.", orderSnapshot.PublicId);
                var refundRequest = new PaymentWebhookRequest
                {
                    OrderId = orderSnapshot.Id,
                    ExternalPaymentId = invoiceId,
                    PaymentProvider = "Monobank",
                    PaymentStatus = PaymentStatus.Refunded,
                    Comment = auditComment
                };
                return await orderService.ProcessPaymentWebhookAsync(refundRequest, cancellationToken);
            }

            case "failure":
            {
                logger.LogWarning("Monobank payment failure for order {OrderPublicId}: {Reason}",
                    orderSnapshot.PublicId, payload.FailureReason ?? normalizedStatus);

                await AppendOrderPaymentAuditUnderLockAsync(
                    orderSnapshot.Id,
                    invoiceId,
                    PaymentStatus.Failed,
                    auditComment,
                    cancellationToken);
                break;
            }

            case "expired":
            {
                logger.LogWarning(
                    "Monobank invoice {InvoiceId} expired for order {OrderPublicId}. Auto-cancelling unpaid New order and returning stock.",
                    invoiceId,
                    orderSnapshot.PublicId);

                var expiredRequest = new PaymentWebhookRequest
                {
                    OrderId = orderSnapshot.Id,
                    ExternalPaymentId = invoiceId,
                    PaymentProvider = "Monobank",
                    PaymentStatus = PaymentStatus.Failed,
                    CancelOrderOnFailure = true,
                    Comment = auditComment
                };

                return await orderService.ProcessPaymentWebhookAsync(expiredRequest, cancellationToken);
            }

            case "processing":
            case "hold":
            case "created":
            {
                logger.LogInformation("Monobank payment for order {OrderPublicId} is in intermediate state: {Status}",
                    orderSnapshot.PublicId, normalizedStatus);

                await AppendOrderPaymentAuditUnderLockAsync(
                    orderSnapshot.Id,
                    invoiceId,
                    PaymentStatus.Pending,
                    auditComment,
                    cancellationToken);
                break;
            }

            default:
                logger.LogWarning("Unrecognized Monobank status '{Status}' for order {OrderPublicId}.",
                    payload.Status, orderSnapshot.PublicId);
                break;
        }

        return null;
    }

    public async Task<MonobankInvoiceStatusResponse> GetInvoiceStatusAsync(
        string invoiceId,
        bool requireOrderMatch = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);
        var cleanId = invoiceId.Trim();

        if (requireOrderMatch)
        {
            await EnsureInvoiceBelongsToOrderAsync(cleanId, cancellationToken);
        }

        var token = GetToken();
        var requestUri = $"{GetBaseUrl()}/api/merchant/invoice/status?invoiceId={Uri.EscapeDataString(cleanId)}";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        requestMessage.Headers.Add("X-Token", token);

        logger.LogInformation("Checking Monobank status for invoice {InvoiceId}", cleanId);

        var response = await httpClient.SendAsync(requestMessage, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Monobank status check failed for invoice {InvoiceId}. Status: {StatusCode}, Body: {Body}",
                cleanId, response.StatusCode, responseContent);
            throw new HttpRequestException($"Помилка перевірки статусу в Monobank: {responseContent}");
        }

        return JsonSerializer.Deserialize<MonobankInvoiceStatusResponse>(responseContent)
            ?? throw new InvalidOperationException("Отримано пусту відповідь від Monobank API.");
    }

    private async Task EnsureInvoiceBelongsToOrderAsync(string invoiceId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Orders
            .AsNoTracking()
            .AnyAsync(o => o.ExternalPaymentId == invoiceId, cancellationToken);

        if (!exists)
        {
            throw new KeyNotFoundException($"Платіж '{invoiceId}' не знайдено.");
        }
    }

    private async Task AppendOrderPaymentAuditUnderLockAsync(
        Guid orderId,
        string invoiceId,
        PaymentStatus? newStatus,
        string auditComment,
        CancellationToken cancellationToken)
    {
        var isRelational = dbContext.Database.IsRelational();
        await using var transaction = isRelational
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var order = isRelational
            ? await dbContext.Orders
                .FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken)
            : await dbContext.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null || order.PaymentStatus == PaymentStatus.Paid)
        {
            return;
        }

        var now = DateTime.UtcNow;
        order.PaymentProvider = "Monobank";
        if (string.IsNullOrWhiteSpace(order.ExternalPaymentId) || order.ExternalPaymentId == invoiceId)
        {
            order.ExternalPaymentId = invoiceId;
            if (newStatus.HasValue)
            {
                order.PaymentStatus = newStatus.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(order.AdminComment))
        {
            order.AdminComment = auditComment;
        }
        else if (!order.AdminComment.Contains(auditComment, StringComparison.Ordinal))
        {
            order.AdminComment = $"{order.AdminComment}\n{auditComment}";
        }

        order.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static (string NormType, string NormMode, long Diff) CalculateDiscountAdjustment(
        long baseAmountKopecks,
        MonobankDiscountDto discount)
    {
        var normType = discount.Type?.Trim().ToLowerInvariant() ?? "";
        if (normType is not ("discount" or "extra"))
        {
            throw new ArgumentException($"Непідтримуваний тип знижки/надбавки: '{discount.Type}'. Очікується 'discount' або 'extra'.");
        }

        var normMode = discount.Mode?.Trim().ToLowerInvariant() ?? "";
        if (normMode is not ("percent" or "value"))
        {
            throw new ArgumentException($"Непідтримуваний режим знижки: '{discount.Mode}'. Очікується 'percent' або 'value'.");
        }

        if (discount.Value <= 0m)
        {
            throw new ArgumentException("Значення знижки або надбавки повинно бути більшим за 0.");
        }

        if (normMode == "percent" && discount.Value > 100m)
        {
            throw new ArgumentException("Відсоткова знижка не може перевищувати 100%.");
        }

        var diff = normMode == "percent"
            ? checked((long)Math.Round(baseAmountKopecks * (discount.Value / 100m), MidpointRounding.AwayFromZero))
            : checked((long)Math.Round(discount.Value, MidpointRounding.AwayFromZero));

        return (normType, normMode, diff);
    }

    private string BuildOrderRedirectUrl(string orderPublicId)
    {
        var baseUrl = ResolveConfiguredRedirectUrl();
        var separator = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{separator}publicId={Uri.EscapeDataString(orderPublicId)}";
    }

    private string ResolveConfiguredRedirectUrl()
    {
        var configured = (configuration["Monobank:RedirectUrl"] ?? configuration["MONOBANK_REDIRECT_URL"])?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && !ContainsPlaceholderDomain(configured))
        {
            return configured;
        }

        var publicSiteUrl = (configuration["Referral:PublicSiteUrl"] ?? DefaultPublicSiteUrl).Trim().TrimEnd('/');
        return $"{publicSiteUrl}/payment-result";
    }

    private string? ResolveConfiguredWebhookUrl()
    {
        var configured = (configuration["Monobank:WebhookUrl"] ?? configuration["MONOBANK_WEBHOOK_URL"])?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && !ContainsPlaceholderDomain(configured))
        {
            return configured;
        }

        var railwayDomain = configuration["RAILWAY_PUBLIC_DOMAIN"]?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(railwayDomain) && !ContainsPlaceholderDomain(railwayDomain))
        {
            var baseUrl = railwayDomain.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || railwayDomain.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? railwayDomain
                : $"https://{railwayDomain}";
            return $"{baseUrl}/api/payments/monobank/webhook";
        }

        return null;
    }

    private static bool ContainsPlaceholderDomain(string url) =>
        url.Contains("mysite.com", StringComparison.OrdinalIgnoreCase);

    private string? ResolveProductIconUrl(Product? product)
    {
        if (product is null || string.IsNullOrWhiteSpace(product.MainImageUrl))
            return null;

        var backendOrigin = ResolvePublicBackendBaseUrl();
        if (!string.IsNullOrWhiteSpace(backendOrigin))
            return $"{backendOrigin}/api/payments/monobank/item-icon/{product.Id:D}.jpg";

        return product.MainImageUrl;
    }

    private string? ResolvePublicBackendBaseUrl()
    {
        var explicitUrl = (configuration["Monobank:PublicBackendUrl"] ?? configuration["BACKEND_PUBLIC_URL"])?.Trim();
        if (TryNormalizePublicHttpsOrigin(explicitUrl, out var origin))
            return origin;

        var railwayDomain = configuration["RAILWAY_PUBLIC_DOMAIN"]?.Trim();
        if (!string.IsNullOrWhiteSpace(railwayDomain))
        {
            var candidate = railwayDomain.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || railwayDomain.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? railwayDomain
                : $"https://{railwayDomain}";
            if (TryNormalizePublicHttpsOrigin(candidate, out origin))
                return origin;
        }

        if (TryNormalizePublicHttpsOrigin(ResolveConfiguredWebhookUrl(), out origin))
            return origin;

        var request = httpContextAccessor?.HttpContext?.Request;
        if (request is not null && request.Host.HasValue)
        {
            var host = request.Host.Host;
            if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                && !host.StartsWith("127.", StringComparison.OrdinalIgnoreCase))
            {
                var scheme = request.IsHttps ? "https" : request.Scheme;
                if (TryNormalizePublicHttpsOrigin($"{scheme}://{request.Host.Value}", out origin))
                    return origin;
            }
        }

        return null;
    }

    private static bool TryNormalizePublicHttpsOrigin(string? url, out string origin)
    {
        origin = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || ContainsPlaceholderDomain(url))
            return false;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.StartsWith("127.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        origin = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
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

            if (forceRefresh && DateTime.UtcNow - _lastPubKeyFetchUtc < PubKeyRefreshCooldown && !string.IsNullOrWhiteSpace(_cachedPublicKey))
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
            _lastPubKeyFetchUtc = DateTime.UtcNow;

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
