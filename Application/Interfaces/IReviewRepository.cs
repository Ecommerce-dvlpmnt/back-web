using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.Interfaces;

public interface IReviewRepository
{
    Task<Review> CreateAsync(Review review, CancellationToken cancellationToken = default);
    Task<Review?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Review>> GetApprovedByProductIdAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Review>> GetAdminReviewsAsync(
        ReviewStatus? status,
        Guid? productId,
        Guid? categoryId,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
    Task<int> GetAdminReviewsCountAsync(
        ReviewStatus? status,
        Guid? productId,
        Guid? categoryId,
        CancellationToken cancellationToken = default);
    Task<Review?> UpdateAsync(Review review, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
