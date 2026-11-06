using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Data.Repositories;

public sealed class SupplierRepository(AppDbContext dbContext) : ISupplierRepository
{
    public async Task<IReadOnlyList<Supplier>> GetAllAsync(
        bool onlyActive,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Suppliers
            .AsNoTracking()
            .AsQueryable();

        if (onlyActive)
            query = query.Where(supplier => supplier.IsActive);

        query = query.OrderBy(supplier => supplier.Name);

        return await query.ToListAsync(cancellationToken);
    }

    public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(supplier => supplier.Id == id, cancellationToken);

    public async Task<Supplier> CreateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        dbContext.Suppliers.Add(supplier);
        await dbContext.SaveChangesAsync(cancellationToken);
        return supplier;
    }

    public Task<bool> ExistsByNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        dbContext.Suppliers
            .AnyAsync(supplier =>
                supplier.Name.ToLower() == name.ToLower() &&
                (!excludeId.HasValue || supplier.Id != excludeId.Value),
                cancellationToken);
}
