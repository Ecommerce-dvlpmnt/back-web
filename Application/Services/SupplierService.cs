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
            throw new InvalidOperationException("Постачальник з такою назвою вже існує.");

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Contact = request.Contact?.Trim(),
            City = request.City?.Trim(),
            Notes = request.Notes?.Trim(),
            IsActive = request.IsActive
        };

        return MapToDto(await supplierRepository.CreateAsync(supplier, cancellationToken));
    }

    public async Task<SupplierDto?> UpdateAsync(
        Guid id,
        UpdateSupplierDto request,
        CancellationToken cancellationToken = default)
    {
        var supplier = await supplierRepository.GetByIdAsync(id, cancellationToken);
        if (supplier is null)
            return null;

        if (await ExistsByNameAsync(request.Name, id, cancellationToken))
            throw new InvalidOperationException("Постачальник з такою назвою вже існує.");

        supplier.Name = request.Name.Trim();
        supplier.Contact = request.Contact?.Trim();
        supplier.City = request.City?.Trim();
        supplier.Notes = request.Notes?.Trim();
        supplier.IsActive = request.IsActive;

        var updated = await supplierRepository.UpdateAsync(supplier, cancellationToken);
        return updated is null ? null : MapToDto(updated);
    }

    public async Task<SupplierDto?> UpdateStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var supplier = await supplierRepository.GetByIdAsync(id, cancellationToken);
        if (supplier is null)
            return null;

        supplier.IsActive = isActive;
        var updated = await supplierRepository.UpdateAsync(supplier, cancellationToken);
        return updated is null ? null : MapToDto(updated);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var supplier = await supplierRepository.GetByIdAsync(id, cancellationToken);
        if (supplier is null)
            return false;

        if (await supplierRepository.HasProductsOrShipmentsAsync(id, cancellationToken))
            throw new InvalidOperationException("Постачальник має пов'язані товари або відправлення і не може бути видалений. Деактивуйте його замість видалення.");

        return await supplierRepository.DeleteAsync(id, cancellationToken);
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
