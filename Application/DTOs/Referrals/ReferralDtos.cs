using System.ComponentModel.DataAnnotations;

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

public sealed record CreatePartnerRequest
{
    [Range(1, long.MaxValue)]
    public long TelegramUserId { get; init; }

    [Required, StringLength(100, MinimumLength = 2)]
    public required string Name { get; init; }

    [Required, StringLength(32, MinimumLength = 2)]
    [RegularExpression(@"^[A-Za-z0-9_-]+$")]
    public required string ReferralCode { get; init; }

    [Range(0.01, 100)]
    public decimal CommissionPercent { get; init; } = 5.0m;
}

public sealed record PartnerResponse(
    Guid Id,
    long TelegramUserId,
    string Name,
    string ReferralCode,
    decimal CommissionPercent,
    decimal TotalEarned,
    DateTime CreatedAt);

public sealed record ReferralCodeValidationResponse(
    bool IsValid,
    string? ReferralCode,
    string? PartnerName);
