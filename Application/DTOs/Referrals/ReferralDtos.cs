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

    [StringLength(30)]
    public string? CardNumber { get; init; }

    [StringLength(150)]
    public string? CardHolderName { get; init; }
}

public sealed record PartnerResponse(
    Guid Id,
    long TelegramUserId,
    string Name,
    string ReferralCode,
    decimal CommissionPercent,
    decimal TotalEarned,
    DateTime CreatedAt,
    decimal PendingBalance = 0m,
    decimal AvailableBalance = 0m,
    int TotalOrders = 0,
    int DeliveredOrders = 0,
    decimal TotalTurnover = 0m,
    string? CardNumber = null,
    string? CardHolderName = null);

public sealed record UpdateReferralPartnerRequest
{
    [Range(0.01, 100)]
    public decimal? CommissionPercent { get; init; }

    [StringLength(100, MinimumLength = 2)]
    public string? Name { get; init; }

    [StringLength(32, MinimumLength = 2)]
    [RegularExpression(@"^[A-Za-z0-9_-]+$")]
    public string? ReferralCode { get; init; }

    [StringLength(30)]
    public string? CardNumber { get; init; }

    [StringLength(150)]
    public string? CardHolderName { get; init; }
}

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

public sealed record BotPayoutRequestDto
{
    [Range(300, 1000000, ErrorMessage = "Мінімальна сума для виведення становить 300 грн.")]
    public decimal Amount { get; init; }

    [Required(ErrorMessage = "Номер банківської картки є обов'язковим.")]
    [RegularExpression(@"^\d{16}$", ErrorMessage = "Номер картки повинен містити рівно 16 цифр.")]
    public required string CardNumber { get; init; }

    public string? CardHolderName { get; init; }
}

public sealed record BotPayoutResponse(
    Guid Id,
    decimal Amount,
    string CardNumberMasked,
    string? CardHolderName,
    PayoutRequestStatus Status,
    decimal RemainingAvailableBalance,
    DateTime CreatedAt);

public sealed record AdminPayoutResponse(
    Guid Id,
    Guid PartnerId,
    long PartnerTelegramUserId,
    string PartnerName,
    string PartnerReferralCode,
    decimal Amount,
    string CardNumber,
    string? CardHolderName,
    PayoutRequestStatus Status,
    string? AdminComment,
    DateTime CreatedAt,
    DateTime? ProcessedAt);

public sealed record UpdatePayoutStatusRequest
{
    [Required(ErrorMessage = "Статус виплати є обов'язковим.")]
    public PayoutRequestStatus Status { get; init; }

    [StringLength(500, ErrorMessage = "Коментар не повинен перевищувати 500 символів.")]
    public string? AdminComment { get; init; }
}
