using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Data.Repositories;

public sealed class CategoryRepository(AppDbContext dbContext) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> GetAllAsync(
        bool? onlyActive,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Categories
            .AsNoTracking()
            .Include(category => category.Parent)
            .AsQueryable();

        if (onlyActive == true)
            query = query.Where(category => category.IsActive);

        query = query
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name);

        return await query.ToListAsync(cancellationToken);
    }

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetCategoryWithParent()
            .FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    public Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        GetCategoryWithParent()
            .FirstOrDefaultAsync(category => category.Slug == slug, cancellationToken);

    public async Task<Category> CreateAsync(Category category, CancellationToken cancellationToken = default)
    {
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<Category?> UpdateAsync(Category category, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Categories
            .FirstOrDefaultAsync(item => item.Id == category.Id, cancellationToken);

        if (existing is null)
            return null;

        dbContext.Entry(existing).CurrentValues.SetValues(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(existing.Id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await dbContext.Categories.FindAsync([id], cancellationToken);
        if (category is null)
            return false;

        dbContext.Categories.Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> ExistsBySlugAsync(
        string slug,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        dbContext.Categories
            .AnyAsync(category => category.Slug == slug &&
                (!excludeId.HasValue || category.Id != excludeId.Value),
                cancellationToken);

    public async Task<bool> HasChildrenOrProductsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var hasChildren = await dbContext.Categories
            .AnyAsync(category => category.ParentId == id, cancellationToken);
        var hasProducts = await dbContext.Products
            .AnyAsync(product => product.CategoryId == id, cancellationToken);

        return hasChildren || hasProducts;
    }

    private IQueryable<Category> GetCategoryWithParent() =>
        dbContext.Categories
            .AsNoTracking()
            .Include(category => category.Parent);
}
