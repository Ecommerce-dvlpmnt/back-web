namespace EcommerceBackend.Application.DTOs.Referrals;

public sealed record ReferralRewardResult(
    Guid OrderId,
    int OrderNumber,
    Guid PartnerId,
    long PartnerTelegramUserId,
    string PartnerName,
    decimal OrderTotal,
    decimal CommissionPercent,
    decimal RewardAmount,
    decimal PartnerTotalEarned);
