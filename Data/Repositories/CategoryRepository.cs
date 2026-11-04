using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Data.Repositories;

// Работает только с данными: ASP.NET Core LINQ -> SQL. Никакой бизнес-логики здесь нет.
public sealed class CategoryRepository(AppDbContext dbContext) : ICategoryRepository
{
    // Список для меню/каталога: сортировка по SortOrder (0..N) зад порядком панели админа.
    public async Task<IReadOnlyList<Category>> GetAllAsync(
        bool? onlyActive,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Categories
            .AsNoTracking()                      // только чтение — EF не будет следить за изменениями (быстрее)
            .Include(category => category.Parent) // нужна вся строка parent'а ради ParentName в DTO
            .AsQueryable();

        if (onlyActive == true)
            query = query.Where(category => category.IsActive);

        query = query
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name);

        return await query.ToListAsync(cancellationToken);
    }

    // GetById с parent'ом — одна выборка вместо двух запросов.
    public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetCategoryWithParent()
            .FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    public Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        GetCategoryWithParent()
            .FirstOrDefaultAsync(category => category.Slug == slug, cancellationToken);

    public async Task<Category> CreateAsync(Category category, CancellationToken cancellationToken = default)
    {
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken); // EF сам подставит сгенерированный БД Id
        return category;
    }

    public async Task<Category?> UpdateAsync(Category category, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Categories
            .FirstOrDefaultAsync(item => item.Id == category.Id, cancellationToken);

        if (existing is null)
            return null;

        // Копирует значения всех столбцов из нашего объекта в отслеживаемый EF — одним разом, без перечисления полей.
        dbContext.Entry(existing).CurrentValues.SetValues(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Возвращаем свежую версию с родителем (для DTO ParentName).
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

    // Slug уникален в таблице: строим выражение (a == slug) и (id != excludeId, если передан).
    public Task<bool> ExistsBySlugAsync(
        string slug,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        dbContext.Categories
            .AnyAsync(category => category.Slug == slug &&
                (!excludeId.HasValue || category.Id != excludeId.Value),
                cancellationToken);

    // Проверка перед удалением: дети (дерево ParentId) или товары (FK RESTRICT не разрешит DELETE).
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
