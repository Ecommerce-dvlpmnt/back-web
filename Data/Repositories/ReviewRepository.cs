using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Data.Repositories;

public sealed class ReviewRepository(AppDbContext dbContext) : IReviewRepository
{
    public async Task<Review> CreateAsync(Review review, CancellationToken cancellationToken = default)
    {
        dbContext.Reviews.Add(review);
        await dbContext.SaveChangesAsync(cancellationToken);
        return review;
    }

    public Task<Review?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Reviews
            .Include(r => r.Product)
            .Include(r => r.Category)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Review>> GetApprovedByProductIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Review>> GetAdminReviewsAsync(
        ReviewStatus? status,
        Guid? productId,
        Guid? categoryId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Reviews
            .AsNoTracking()
            .Include(r => r.Product)
            .Include(r => r.Category)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        if (productId.HasValue)
            query = query.Where(r => r.ProductId == productId.Value);

        if (categoryId.HasValue)
            query = query.Where(r => r.CategoryId == categoryId.Value);

        query = query.OrderByDescending(r => r.CreatedAt);

        return await query
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> GetAdminReviewsCountAsync(
        ReviewStatus? status,
        Guid? productId,
        Guid? categoryId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Reviews.AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        if (productId.HasValue)
            query = query.Where(r => r.ProductId == productId.Value);

        if (categoryId.HasValue)
            query = query.Where(r => r.CategoryId == categoryId.Value);

        return query.CountAsync(cancellationToken);
    }

    public async Task<Review?> UpdateAsync(Review review, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Reviews.FindAsync([review.Id], cancellationToken);
        if (existing is null)
            return null;

        dbContext.Entry(existing).CurrentValues.SetValues(review);
        await dbContext.SaveChangesAsync(cancellationToken);

        return existing;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var review = await dbContext.Reviews.FindAsync([id], cancellationToken);
        if (review is null)
            return false;

        dbContext.Reviews.Remove(review);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
