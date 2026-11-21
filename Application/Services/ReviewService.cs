using EcommerceBackend.Application.DTOs.Reviews;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.Services;

public sealed class ReviewService(
    IReviewRepository reviewRepository,
    IProductRepository productRepository,
    IAuditLogService auditLogService) : IReviewService
{
    public async Task<ReviewDto> CreateReviewAsync(
        CreateReviewDto request,
        CancellationToken cancellationToken = default)
    {
        // Защита от спам-ботов через скрытое поле Honeypot.
        // Если бот заполнил невидимое поле, отклоняем запрос.
        if (!string.IsNullOrWhiteSpace(request.Honeypot))
        {
            throw new ArgumentException("Виявлено спам-активність.");
        }

        var product = await productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            throw new KeyNotFoundException($"Товар з ID '{request.ProductId}' не знайдено.");
        }

        var review = new Review
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            CategoryId = product.CategoryId, // Автоматически индексируем категорию товара
            AuthorName = request.AuthorName.Trim(),
            Rating = request.Rating,
            Comment = request.Comment.Trim(),
            Pros = string.IsNullOrWhiteSpace(request.Pros) ? null : request.Pros.Trim(),
            Cons = string.IsNullOrWhiteSpace(request.Cons) ? null : request.Cons.Trim(),
            Status = ReviewStatus.Pending, // По умолчанию отзыв всегда ждет подтверждения модератора
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await reviewRepository.CreateAsync(review, cancellationToken);
        return MapToDto(created, product.Name);
    }

    public async Task<ProductReviewsSummaryDto> GetProductReviewsAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var approvedReviews = await reviewRepository.GetApprovedByProductIdAsync(productId, cancellationToken);

        var total = approvedReviews.Count;
        var avg = total > 0 ? Math.Round(approvedReviews.Average(r => r.Rating), 1) : 0.0;

        var distribution = new Dictionary<int, int>
        {
            [5] = approvedReviews.Count(r => r.Rating == 5),
            [4] = approvedReviews.Count(r => r.Rating == 4),
            [3] = approvedReviews.Count(r => r.Rating == 3),
            [2] = approvedReviews.Count(r => r.Rating == 2),
            [1] = approvedReviews.Count(r => r.Rating == 1)
        };

        return new ProductReviewsSummaryDto
        {
            ProductId = productId,
            TotalReviews = total,
            AverageRating = avg,
            RatingDistribution = distribution,
            Reviews = approvedReviews.Select(r => MapToDto(r, r.Product?.Name)).ToList()
        };
    }

    public async Task<IReadOnlyList<ReviewDto>> GetAdminReviewsAsync(
        ReviewStatus? status,
        Guid? productId,
        Guid? categoryId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safePageSize = pageSize is < 1 or > 100 ? 50 : pageSize;
        var skip = (safePage - 1) * safePageSize;

        var reviews = await reviewRepository.GetAdminReviewsAsync(
            status,
            productId,
            categoryId,
            skip,
            safePageSize,
            cancellationToken);

        return reviews.Select(r => MapToDto(r, r.Product?.Name, r.Category?.Name)).ToList();
    }

    public async Task<ReviewDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var review = await reviewRepository.GetByIdAsync(id, cancellationToken);
        return review is null ? null : MapToDto(review, review.Product?.Name, review.Category?.Name);
    }

    public async Task<ReviewDto?> UpdateStatusAsync(
        Guid id,
        UpdateReviewStatusDto request,
        CancellationToken cancellationToken = default)
    {
        var review = await reviewRepository.GetByIdAsync(id, cancellationToken);
        if (review is null)
            return null;

        var oldStatus = review.Status;
        var oldAdminComment = review.AdminComment;

        review.Status = request.Status;
        review.AdminComment = string.IsNullOrWhiteSpace(request.AdminComment) ? null : request.AdminComment.Trim();

        if (oldStatus != review.Status)
        {
            await auditLogService.LogAsync(
                AuditLogActions.ReviewStatusChanged,
                AuditLogEntityTypes.Review,
                review.Id.ToString(),
                new
                {
                    productId = review.ProductId,
                    before = new { status = oldStatus.ToString(), adminComment = oldAdminComment },
                    after = new { status = review.Status.ToString(), adminComment = review.AdminComment },
                    changedFields = new[] { "status" }
                },
                cancellationToken);
        }

        var updated = await reviewRepository.UpdateAsync(review, cancellationToken);
        return updated is null ? null : MapToDto(updated, updated.Product?.Name, updated.Category?.Name);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        reviewRepository.DeleteAsync(id, cancellationToken);

    private static ReviewDto MapToDto(Review review, string? productName = null, string? categoryName = null) => new()
    {
        Id = review.Id,
        ProductId = review.ProductId,
        ProductName = productName ?? review.Product?.Name,
        CategoryId = review.CategoryId,
        CategoryName = categoryName ?? review.Category?.Name,
        AuthorName = review.AuthorName,
        Rating = review.Rating,
        Comment = review.Comment,
        Pros = review.Pros,
        Cons = review.Cons,
        Status = review.Status,
        AdminComment = review.AdminComment,
        CreatedAt = review.CreatedAt
    };
}
