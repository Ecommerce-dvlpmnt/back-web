using EcommerceBackend.Application.DTOs.Suppliers;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;

namespace EcommerceBackend.Application.Services;

public sealed class SupplierService(ISupplierRepository supplierRepository) : ISupplierService
{
    public async Task<IReadOnlyList<SupplierDto>> GetAllAsync(
        bool onlyActive,
        CancellationToken cancellationToken = default)
    {
        var suppliers = await supplierRepository.GetAllAsync(onlyActive, cancellationToken);
        return suppliers.Select(MapToDto).ToList();
    }

    public async Task<SupplierDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var supplier = await supplierRepository.GetByIdAsync(id, cancellationToken);
        return supplier is null ? null : MapToDto(supplier);
    }

    public async Task<SupplierDto> CreateAsync(CreateSupplierDto request, CancellationToken cancellationToken = default)
    {
        if (await ExistsByNameAsync(request.Name, null, cancellationToken))
            throw new InvalidOperationException("A supplier with this name already exists.");

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Contact = request.Contact,
            City = request.City,
            Notes = request.Notes,
            IsActive = request.IsActive
        };

        return MapToDto(await supplierRepository.CreateAsync(supplier, cancellationToken));
    }

    public Task<bool> ExistsByNameAsync(
        string name,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        supplierRepository.ExistsByNameAsync(name, excludeId, cancellationToken);

    private static SupplierDto MapToDto(Supplier supplier) => new()
    {
        Id = supplier.Id,
        Name = supplier.Name,
        Contact = supplier.Contact,
        City = supplier.City,
        Notes = supplier.Notes,
        IsActive = supplier.IsActive
    };
}
