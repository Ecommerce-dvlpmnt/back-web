using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EcommerceBackend.Application.Services;

public sealed class MonobankService(
    HttpClient httpClient,
    IOrderService orderService,
    IConfiguration configuration,
    ILogger<MonobankService> logger) : IMonobankService
{
    private const string DefaultBaseUrl = "https://api.monobank.ua";

    public async Task<MonobankInvoiceResponse> CreateInvoiceForOrderAsync(
        string orderPublicId,
        string? customRedirectUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderPublicId);

        var order = await orderService.GetByPublicIdAsync(orderPublicId, cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException($"Замовлення '{orderPublicId}' не знайдено.");
        }

        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            throw new InvalidOperationException($"Замовлення '{orderPublicId}' вже успішно оплачене.");
        }

        var token = configuration["Monobank:Token"]
            ?? configuration["MONOBANK_TOKEN"]
            ?? configuration["MONOBANK__TOKEN"];

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Monobank API Token не налаштовано в конфігурації (Monobank:Token).");
        }

        var redirectUrl = customRedirectUrl
            ?? configuration["Monobank:RedirectUrl"]
            ?? configuration["MONOBANK_REDIRECT_URL"]
            ?? $"{configuration["Referral:PublicSiteUrl"] ?? "https://www.jingledecor.com.ua"}/payment-result";

        var webhookUrl = configuration["Monobank:WebhookUrl"]
            ?? configuration["MONOBANK_WEBHOOK_URL"];

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

        logger.LogInformation("Creating Monobank invoice for order {OrderPublicId}, amount {Amount} kop.",
            order.PublicId, amountInKopecks);

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

    public async Task<OrderPaymentConfirmationResponse?> ProcessWebhookAsync(
        MonobankWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        logger.LogInformation("Received Monobank webhook: InvoiceId={InvoiceId}, Status={Status}, Reference={Reference}",
            payload.InvoiceId, payload.Status, payload.Reference);

        if (string.IsNullOrWhiteSpace(payload.Reference))
        {
            logger.LogWarning("Monobank webhook without Reference (order public ID). Ignoring.");
            return null;
        }

        PaymentStatus mappedStatus = payload.Status.ToLowerInvariant() switch
        {
            "success" => PaymentStatus.Paid,
            "failure" => PaymentStatus.Failed,
            "reversed" => PaymentStatus.Refunded,
            "expired" => PaymentStatus.Failed,
            _ => PaymentStatus.Pending
        };

        if (mappedStatus == PaymentStatus.Pending)
        {
            logger.LogInformation("Monobank status '{Status}' does not require order payment status update.", payload.Status);
            return null;
        }

        var webhookRequest = new PaymentWebhookRequest
        {
            OrderPublicId = payload.Reference,
            ExternalPaymentId = payload.InvoiceId,
            PaymentProvider = "Monobank",
            PaymentStatus = mappedStatus
        };

        return await orderService.ProcessPaymentWebhookAsync(webhookRequest, cancellationToken);
    }

    public async Task<MonobankInvoiceStatusResponse> GetInvoiceStatusAsync(
        string invoiceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceId);

        var token = configuration["Monobank:Token"]
            ?? configuration["MONOBANK_TOKEN"]
            ?? configuration["MONOBANK__TOKEN"];

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Monobank API Token не налаштовано в конфігурації (Monobank:Token).");
        }

        var requestUri = $"{GetBaseUrl()}/api/merchant/invoice/status?invoiceId={Uri.EscapeDataString(invoiceId)}";
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUri);
        requestMessage.Headers.Add("X-Token", token);

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

    private string GetBaseUrl()
    {
        return configuration["Monobank:BaseUrl"]?.TrimEnd('/') ?? DefaultBaseUrl;
    }
}
