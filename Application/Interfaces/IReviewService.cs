using EcommerceBackend.Application.DTOs.Reviews;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.Interfaces;

public interface IReviewService
{
    Task<ReviewDto> CreateReviewAsync(CreateReviewDto request, CancellationToken cancellationToken = default);
    Task<ProductReviewsSummaryDto> GetProductReviewsAsync(Guid productId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReviewDto>> GetAdminReviewsAsync(
        ReviewStatus? status,
        Guid? productId,
        Guid? categoryId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
    Task<ReviewDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ReviewDto?> UpdateStatusAsync(Guid id, UpdateReviewStatusDto request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
