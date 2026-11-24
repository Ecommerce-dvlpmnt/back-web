using EcommerceBackend.Application.DTOs.Orders;

namespace EcommerceBackend.Application.Interfaces;

/// <summary>Service for integrating with Monobank Acquiring API.</summary>
public interface IMonobankService
{
    /// <summary>Creates a payment invoice in Monobank for an existing order.</summary>
    Task<MonobankInvoiceResponse> CreateInvoiceForOrderAsync(
        string orderPublicId,
        string? customRedirectUrl = null,
        string? customWebhookUrl = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a standalone test/custom invoice in Monobank.</summary>
    Task<MonobankInvoiceResponse> CreateCustomInvoiceAsync(
        CreateCustomMonobankInvoiceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies the ECDSA signature sent by Monobank in the X-Sign header against the raw body bytes.</summary>
    Task<bool> VerifyWebhookSignatureAsync(
        byte[] rawBodyBytes,
        string? xSignBase64,
        CancellationToken cancellationToken = default);

    /// <summary>Processes a verified incoming webhook from Monobank, updating order status in DB and sending notifications.</summary>
    Task<OrderPaymentConfirmationResponse?> ProcessWebhookAsync(
        MonobankWebhookPayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>Queries the live status of an invoice from Monobank API.</summary>
    Task<MonobankInvoiceStatusResponse> GetInvoiceStatusAsync(
        string invoiceId,
        CancellationToken cancellationToken = default);
}
