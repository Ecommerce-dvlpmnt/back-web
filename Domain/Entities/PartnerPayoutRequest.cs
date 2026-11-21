using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Domain.Entities;

public class PartnerPayoutRequest
{
    public Guid Id { get; set; }
    public Guid PartnerId { get; set; }
    public decimal Amount { get; set; }
    public string CardNumber { get; set; } = null!;
    public string? CardHolderName { get; set; }
    public PayoutRequestStatus Status { get; set; } = PayoutRequestStatus.Pending;
    public string? AdminComment { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }

    public Partner? Partner { get; set; }
}
