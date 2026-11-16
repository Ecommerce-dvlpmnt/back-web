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

    public async Task<Supplier?> UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.Suppliers.FindAsync([supplier.Id], cancellationToken);
        if (existing is null)
            return null;

        dbContext.Entry(existing).CurrentValues.SetValues(supplier);
        await dbContext.SaveChangesAsync(cancellationToken);

        return existing;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var supplier = await dbContext.Suppliers.FindAsync([id], cancellationToken);
        if (supplier is null)
            return false;

        dbContext.Suppliers.Remove(supplier);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> HasProductsOrShipmentsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var hasProducts = await dbContext.Products
            .AnyAsync(product => product.SupplierId == id, cancellationToken);
        var hasShipments = await dbContext.OrderShipments
            .AnyAsync(shipment => shipment.SupplierId == id, cancellationToken);

        return hasProducts || hasShipments;
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
