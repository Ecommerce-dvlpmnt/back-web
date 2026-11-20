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

    Task<PartnerResponse> UpdatePartnerAsync(
        Guid id,
        UpdateReferralPartnerRequest request,
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

    Task<PromoCodeResponse> CreatePromoCodeAsync(
        CreatePromoCodeRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<PromoCodeResponse>> GetAllPromoCodesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PromoCodeResponse>>(Array.Empty<PromoCodeResponse>());

    Task<bool> DeactivatePromoCodeAsync(
        Guid promoCodeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    Task<IReadOnlyList<AdminBotOrderNotificationDto>> GetRecentOrdersForAdminBotAsync(
        int limit = 10,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AdminBotOrderNotificationDto>>(Array.Empty<AdminBotOrderNotificationDto>());

    async Task<AdminBotOrdersPageDto> GetAdminBotOrdersPageAsync(
        string filter = "all",
        int page = 0,
        int pageSize = 5,
        CancellationToken cancellationToken = default)
    {
        var all = await GetRecentOrdersForAdminBotAsync(50, cancellationToken);
        var clampedSize = Math.Clamp(pageSize, 1, 20);
        var clampedPage = Math.Max(0, page);
        var items = all.Skip(clampedPage * clampedSize).Take(clampedSize).ToArray();
        return new AdminBotOrdersPageDto(
            filter,
            clampedPage,
            clampedSize,
            all.Count,
            all.Sum(x => x.TotalPrice),
            items);
    }

    async Task<AdminBotOrderNotificationDto?> GetAdminBotOrderByNumberAsync(
        int orderNumber,
        CancellationToken cancellationToken = default)
    {
        var all = await GetRecentOrdersForAdminBotAsync(50, cancellationToken);
        return all.FirstOrDefault(x => x.OrderNumber == orderNumber);
    }
}
