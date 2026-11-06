using EcommerceBackend.Application.DTOs.Suppliers;

namespace EcommerceBackend.Application.Interfaces;

public interface ISupplierService
{
    Task<IReadOnlyList<SupplierDto>> GetAllAsync(bool onlyActive, CancellationToken cancellationToken = default);
    Task<SupplierDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SupplierDto> CreateAsync(CreateSupplierDto request, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(string name, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
