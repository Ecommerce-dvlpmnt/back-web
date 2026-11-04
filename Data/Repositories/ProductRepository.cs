using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Data.Repositories;

public sealed class ProductRepository(AppDbContext dbContext) : IProductRepository
{
    public async Task<PagedResult<Product>> GetAllAsync(
        ProductQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = Math.Max(1, query.PageNumber);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var skip = (int)Math.Min((long)(pageNumber - 1) * pageSize, int.MaxValue);
        var products = dbContext.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .Include(product => product.Images.OrderBy(image => image.SortOrder))
            .AsQueryable();

        if (query.CategoryId.HasValue)
            products = products.Where(product => product.CategoryId == query.CategoryId.Value);

        if (query.SupplierId.HasValue)
            products = products.Where(product => product.SupplierId == query.SupplierId.Value);

        if (query.OnlyActive == true)
            products = products.Where(product => product.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            products = products.Where(product =>
                EF.Functions.ILike(product.Name, $"%{search}%") ||
                EF.Functions.ILike(product.Sku, $"%{search}%"));
        }

        products = (query.SortBy ?? "date_desc").ToLowerInvariant() switch
        {
            "price_asc" => products.OrderBy(product => product.Price),
            "price_desc" => products.OrderByDescending(product => product.Price),
            "date_asc" => products.OrderBy(product => product.CreatedAt),
            "date_desc" => products.OrderByDescending(product => product.CreatedAt),
            _ => products.OrderByDescending(product => product.CreatedAt)
        };

        var totalCount = await products.CountAsync(cancellationToken);
        var items = await products
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Product>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetProductsWithDetails()
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    public Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        GetProductsWithDetails()
            .FirstOrDefaultAsync(product => product.Slug == slug, cancellationToken);

    public async Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default)
    {
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        return product;
    }

    public async Task<Product?> UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Products
            .Include(item => item.Images)
            .FirstOrDefaultAsync(item => item.Id == product.Id, cancellationToken);

        if (existing is null)
            return null;

        dbContext.Entry(existing).CurrentValues.SetValues(product);
        dbContext.ProductImages.RemoveRange(existing.Images);
        existing.Images.Clear();

        foreach (var image in product.Images)
        {
            image.ProductId = existing.Id;
            existing.Images.Add(image);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(existing.Id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products.FindAsync([id], cancellationToken);
        if (product is null)
            return false;

        dbContext.Products.Remove(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> ExistsBySkuOrSlugAsync(
        string sku,
        string slug,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        dbContext.Products.AnyAsync(product =>
            (product.Sku == sku || product.Slug == slug) &&
            (!excludeId.HasValue || product.Id != excludeId.Value),
            cancellationToken);

    private IQueryable<Product> GetProductsWithDetails() =>
        dbContext.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .Include(product => product.Images.OrderBy(image => image.SortOrder));
}
