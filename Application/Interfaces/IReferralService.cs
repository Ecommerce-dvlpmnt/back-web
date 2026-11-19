using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Domain.Entities;

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
}
