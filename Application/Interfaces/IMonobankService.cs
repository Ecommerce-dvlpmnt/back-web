using EcommerceBackend.Application.DTOs.Orders;

namespace EcommerceBackend.Application.Interfaces;

/// <summary>Service for integrating with Monobank Acquiring API.</summary>
public interface IMonobankService
{
    /// <summary>Creates a payment invoice in Monobank for an existing order.</summary>
    Task<MonobankInvoiceResponse> CreateInvoiceForOrderAsync(
        string orderPublicId,
        string? customRedirectUrl = null,
        CancellationToken cancellationToken = default);

    /// <summary>Processes an incoming webhook from Monobank, updating order status and triggers notifications.</summary>
    Task<OrderPaymentConfirmationResponse?> ProcessWebhookAsync(
        MonobankWebhookPayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>Queries the live status of an invoice from Monobank API.</summary>
    Task<MonobankInvoiceStatusResponse> GetInvoiceStatusAsync(
        string invoiceId,
        CancellationToken cancellationToken = default);
}
