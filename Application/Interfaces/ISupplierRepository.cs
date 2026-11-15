using EcommerceBackend.Domain.Entities;

namespace EcommerceBackend.Application.Interfaces;

public interface ISupplierRepository
{
    Task<IReadOnlyList<Supplier>> GetAllAsync(bool onlyActive, CancellationToken cancellationToken = default);
    Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Supplier> CreateAsync(Supplier supplier, CancellationToken cancellationToken = default);
    Task<Supplier?> UpdateAsync(Supplier supplier, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> HasProductsOrShipmentsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
