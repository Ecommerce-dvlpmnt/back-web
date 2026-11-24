using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.DTOs.Orders;

/// <summary>Request payload for creating a custom or standalone test invoice (Admin only).</summary>
public sealed record CreateCustomMonobankInvoiceRequest
{
    /// <summary>Amount in kopecks (e.g. 10000 = 100.00 UAH). Default: 10000 (100 грн).</summary>
    [Range(100, 100_000_000)]
    public long AmountInKopecks { get; init; } = 10000;

    /// <summary>Payment description / destination. Default: "Тестова оплата".</summary>
    [Required, StringLength(255, MinimumLength = 1)]
    public string Destination { get; init; } = "Тестова оплата";

    /// <summary>Optional redirect URL override for admin tests.</summary>
    public string? RedirectUrl { get; init; }

    /// <summary>Optional webhook URL override for admin tests.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>Optional test reference ID (cannot collide with real order PublicId).</summary>
    [StringLength(64)]
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

/// <summary>Safe order payment status summary for the frontend /payment-result page.</summary>
public sealed record MonobankOrderPaymentStatusResponse(
    string OrderPublicId,
    int OrderNumber,
    decimal TotalPrice,
    PaymentStatus PaymentStatus,
    OrderStatus OrderStatus,
    string? InvoiceId,
    bool IsPaid,
    bool CanRetryPayment,
    bool ReceiptAvailable);

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
