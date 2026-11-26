using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
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
            .Include(product => product.Reviews.Where(review => review.Status == ReviewStatus.Approved))
            .AsQueryable();

        if (query.CategoryId.HasValue)
            products = products.Where(product => product.CategoryId == query.CategoryId.Value);

        if (query.CategoryIds is { Count: > 0 })
            products = products.Where(product => query.CategoryIds.Contains(product.CategoryId));

        if (query.SupplierId.HasValue)
            products = products.Where(product => product.SupplierId == query.SupplierId.Value);

        if (query.OnlyActive == true)
            products = products.Where(product => product.IsActive);

        if (query.MinPrice.HasValue)
            products = products.Where(product => product.Price >= query.MinPrice.Value);
        if (query.MaxPrice.HasValue)
            products = products.Where(product => product.Price <= query.MaxPrice.Value);
        if (query.InStock == true)
            products = products.Where(product => product.Stock > 0);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            products = products.Where(product =>
                EF.Functions.ILike(product.Name, $"%{search}%") ||
                EF.Functions.ILike(product.Sku, $"%{search}%"));
        }

        products = (query.SortBy ?? "date_desc").ToLowerInvariant() switch
        {
            "price_asc" => products.OrderBy(product => product.Price).ThenBy(product => product.Id),
            "price_desc" => products.OrderByDescending(product => product.Price).ThenBy(product => product.Id),
            "date_asc" => products.OrderBy(product => product.CreatedAt).ThenBy(product => product.Id),
            "date_desc" => products.OrderByDescending(product => product.CreatedAt).ThenBy(product => product.Id),
            _ => products.OrderByDescending(product => product.CreatedAt).ThenBy(product => product.Id)
        };

        var totalCount = await products.CountAsync(cancellationToken);
        var page = products.Skip(skip).Take(pageSize);
        // Project before materialization: no descriptions, JSON attributes, SEO fields,
        // supplier data or image collections are read for storefront cards.
        var items = query.Summary
            ? await page.Select(product => new Product
            {
                Id = product.Id,
                CategoryId = product.CategoryId,
                Category = new Category { Name = product.Category.Name },
                Sku = product.Sku,
                Name = product.Name,
                Slug = product.Slug,
                Price = product.Price,
                OldPrice = product.OldPrice,
                Stock = product.Stock,
                MainImageUrl = product.MainImageUrl,
                IsActive = product.IsActive,
                UpdatedAt = product.UpdatedAt
            }).ToListAsync(cancellationToken)
            : await page
                .Include(product => product.Category)
                .Include(product => product.Images.OrderBy(image => image.SortOrder))
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
            dbContext.ProductImages.Add(image);
            existing.Images.Add(image);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(existing.Id, cancellationToken);
    }

    public async Task<Product?> UpdateImageAsync(
        Guid productId,
        string? mainImageUrl,
        ProductImage? image,
        CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products
            .Include(item => item.Images)
            .FirstOrDefaultAsync(item => item.Id == productId, cancellationToken);

        if (product is null)
            return null;

        if (image is null)
            product.MainImageUrl = mainImageUrl;
        else
        {
            dbContext.ProductImages.Add(image);
            product.Images.Add(image);
        }

        product.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(productId, cancellationToken);
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

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
            return false;

        product.IsActive = false;
        product.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> HasOrdersAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.OrderItems.AnyAsync(item => item.ProductId == id, cancellationToken);

    public async Task<bool> UpdateStockAsync(Guid id, int stock, CancellationToken cancellationToken = default)
    {
        var product = await dbContext.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
            return false;

        product.Stock = stock;
        product.StockUpdatedAt = DateTime.UtcNow;
        product.UpdatedAt = DateTime.UtcNow;
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
            .Include(product => product.Images.OrderBy(image => image.SortOrder))
            .Include(product => product.Reviews.Where(review => review.Status == ReviewStatus.Approved))
            .Include(product => product.RelatedLinks)
                .ThenInclude(r => r.RelatedProduct);
}
