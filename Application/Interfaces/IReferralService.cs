using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.Interfaces;

public interface IReferralService
{
    Task<Partner?> FindPartnerByCodeAsync(
        string? referralCode,
        CancellationToken cancellationToken = default);

    Task<bool> AttachReferrerAsync(
        Order order,
        string? referralCode,
        CancellationToken cancellationToken = default);

    Task<ReferralRewardResult?> ProcessPaidOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<ReferralRewardResult?> ProcessDeliveredOrderRewardAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<ReferralRewardResult?> ProcessCancelledOrderRewardAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PartnerResponse>> GetAllPartnersAsync(
        CancellationToken cancellationToken = default);

    Task<PartnerResponse?> GetPartnerByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PartnerResponse> CreatePartnerAsync(
        CreatePartnerRequest request,
        CancellationToken cancellationToken = default);

    Task<ReferralCodeValidationResponse> ValidateCodeAsync(
        string? referralCode,
        CancellationToken cancellationToken = default);

    Task<BotPartnerProfileResponse?> GetBotPartnerProfileAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);

    Task<BotPartnerProfileResponse> RegisterBotPartnerAsync(
        BotRegisterPartnerRequest request,
        CancellationToken cancellationToken = default);

    Task<BotPartnerStatsResponse?> GetBotPartnerStatsAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);

    Task<BotPartnerOrdersResponse?> GetBotPartnerOrdersAsync(
        long telegramUserId,
        int limit = 20,
        int offset = 0,
        CancellationToken cancellationToken = default);

    Task<BotPayoutResponse> RequestPayoutAsync(
        long telegramUserId,
        BotPayoutRequestDto request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BotPayoutResponse>> GetBotPartnerPayoutsAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminPayoutResponse>> GetAllPayoutRequestsAsync(
        PayoutRequestStatus? status = null,
        CancellationToken cancellationToken = default);

    Task<AdminPayoutResponse> ProcessPayoutRequestAsync(
        Guid payoutId,
        UpdatePayoutStatusRequest request,
        CancellationToken cancellationToken = default);
}
