namespace EcommerceBackend.Domain.Entities;

public class Partner
{
    public Guid Id { get; set; }
    public long TelegramUserId { get; set; }
    public string Name { get; set; } = null!;
    public string ReferralCode { get; set; } = null!;
    public decimal CommissionPercent { get; set; } = 5.0m;
    public decimal TotalEarned { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<PartnerPayoutRequest> PayoutRequests { get; set; } = new List<PartnerPayoutRequest>();
}
