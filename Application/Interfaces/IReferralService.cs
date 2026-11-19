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
}
