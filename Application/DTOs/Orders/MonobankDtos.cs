using System.Text.Json.Serialization;

namespace EcommerceBackend.Application.DTOs.Orders;

/// <summary>Request payload for initiating a Monobank acquiring payment for an order.</summary>
public sealed record CreateMonobankPaymentRequest
{
    /// <summary>Optional custom redirect URL after payment. If not set, defaults to server configuration.</summary>
    public string? RedirectUrl { get; init; }

    /// <summary>Optional custom webhook URL. If not set, defaults to server configuration.</summary>
    public string? WebhookUrl { get; init; }
}

/// <summary>Request payload for creating a custom or standalone test invoice.</summary>
public sealed record CreateCustomMonobankInvoiceRequest
{
    /// <summary>Amount in kopecks (e.g. 10000 = 100.00 UAH). Default: 10000 (100 грн).</summary>
    public long AmountInKopecks { get; init; } = 10000;

    /// <summary>Payment description / destination. Default: "Тестова оплата".</summary>
    public string Destination { get; init; } = "Тестова оплата";

    /// <summary>Redirect URL after payment. Default: https://mysite.com/payment-result.</summary>
    public string RedirectUrl { get; init; } = "https://mysite.com/payment-result";

    /// <summary>Webhook URL for receiving callbacks. Default: https://mysite.com/api/payment/webhook.</summary>
    public string WebhookUrl { get; init; } = "https://mysite.com/api/payment/webhook";

    /// <summary>Optional reference ID (e.g. order number).</summary>
    public string? Reference { get; init; }
}

/// <summary>Response containing payment redirection details for the customer.</summary>
public sealed record MonobankInvoiceResponse
{
    [JsonPropertyName("invoiceId")]
    public required string InvoiceId { get; init; }

    [JsonPropertyName("pageUrl")]
    public required string PageUrl { get; init; }
}

/// <summary>Payload received from Monobank webhook upon payment completion or status change.</summary>
public sealed record MonobankWebhookPayload
{
    [JsonPropertyName("invoiceId")]
    public required string InvoiceId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }

    [JsonPropertyName("amount")]
    public long Amount { get; init; }

    [JsonPropertyName("ccy")]
    public int Ccy { get; init; }

    [JsonPropertyName("finalAmount")]
    public long? FinalAmount { get; init; }

    [JsonPropertyName("createdDate")]
    public string? CreatedDate { get; init; }

    [JsonPropertyName("modifiedDate")]
    public string? ModifiedDate { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("cancelList")]
    public IReadOnlyList<object>? CancelList { get; init; }

    [JsonPropertyName("walletData")]
    public object? WalletData { get; init; }
}

/// <summary>Status response for an invoice inquiry from Monobank API.</summary>
public sealed record MonobankInvoiceStatusResponse
{
    [JsonPropertyName("invoiceId")]
    public required string InvoiceId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }

    [JsonPropertyName("amount")]
    public long Amount { get; init; }

    [JsonPropertyName("ccy")]
    public int Ccy { get; init; }

    [JsonPropertyName("finalAmount")]
    public long? FinalAmount { get; init; }

    [JsonPropertyName("createdDate")]
    public string? CreatedDate { get; init; }

    [JsonPropertyName("modifiedDate")]
    public string? ModifiedDate { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }
}

/// <summary>Response from GET /api/merchant/pubkey.</summary>
public sealed record MonobankPubKeyResponse
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }
}
