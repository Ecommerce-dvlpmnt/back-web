using EcommerceBackend.Application.DTOs.Finance;

namespace EcommerceBackend.Application.Interfaces;

public interface IFinanceService
{
    Task<PartnersSummaryResponse> GetPartnersSummaryAsync(CancellationToken cancellationToken = default);

    Task<AdSpendResponse> AddAdSpendAsync(
        CreateAdSpendRequest request,
        Guid? adminId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdSpendResponse>> GetAdSpendsAsync(CancellationToken cancellationToken = default);

    Task<bool> DeleteAdSpendAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PartnerDepositResponse> AddDepositAsync(
        CreateDepositRequest request,
        Guid? currentAdminId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PartnerDepositResponse>> GetDepositsAsync(CancellationToken cancellationToken = default);

    Task<bool> UpdatePartnerSettingsAsync(
        Guid adminId,
        UpdatePartnerSettingsRequest request,
        CancellationToken cancellationToken = default);
}
