using System.ComponentModel.DataAnnotations;
using EcommerceBackend.Domain.Enums;

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
    decimal PartnerTotalEarned,
    ReferralRewardStatus Status = ReferralRewardStatus.Paid);

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
    DateTime CreatedAt,
    decimal PendingBalance = 0m);

public sealed record ReferralCodeValidationResponse(
    bool IsValid,
    string? ReferralCode,
    string? PartnerName);

public sealed record BotRegisterPartnerRequest
{
    [Range(1, long.MaxValue)]
    public long TelegramUserId { get; init; }

    [Required, StringLength(100, MinimumLength = 2)]
    public required string Name { get; init; }

    [StringLength(32, MinimumLength = 2)]
    [RegularExpression(@"^[A-Za-z0-9_-]+$")]
    public string? ReferralCode { get; init; }
}

public sealed record BotPartnerProfileResponse(
    Guid Id,
    long TelegramUserId,
    string Name,
    string ReferralCode,
    decimal CommissionPercent,
    decimal AvailableBalance,
    decimal PendingBalance,
    int TotalOrdersCount,
    int DeliveredOrdersCount,
    DateTime CreatedAt);

public sealed record BotPartnerStatsResponse(
    string PartnerName,
    string ReferralCode,
    decimal CommissionPercent,
    decimal AvailableBalance,
    decimal PendingBalance,
    decimal TotalTurnover,
    int TotalOrders,
    int DeliveredOrders,
    int PendingOrders,
    int CancelledOrders);

public sealed record BotPartnerOrderDto(
    int OrderNumber,
    string PublicId,
    decimal TotalPrice,
    decimal RewardAmount,
    OrderStatus OrderStatus,
    PaymentStatus PaymentStatus,
    ReferralRewardStatus RewardStatus,
    DateTime CreatedAt);

public sealed record BotPartnerOrdersResponse(
    int TotalCount,
    IReadOnlyList<BotPartnerOrderDto> Items);
