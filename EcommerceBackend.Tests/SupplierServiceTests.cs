using EcommerceBackend.Application.DTOs.Suppliers;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Domain.Entities;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class SupplierServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesSupplierSuccessfully()
    {
        var repository = new FakeSupplierRepository();
        var service = new SupplierService(repository);

        var result = await service.CreateAsync(new CreateSupplierDto
        {
            Name = "Global Decor",
            Contact = "John Doe",
            City = "Kyiv",
            Notes = "Fast shipping",
            IsActive = true
        });

        Assert.NotNull(repository.Created);
        Assert.Equal("Global Decor", repository.Created!.Name);
        Assert.Equal("Kyiv", repository.Created.City);
        Assert.True(repository.Created.IsActive);
        Assert.Equal(repository.Created.Id, result.Id);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateName()
    {
        var repository = new FakeSupplierRepository();
        repository.ExistingNames.Add("global decor");
        var service = new SupplierService(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateSupplierDto { Name = "Global Decor" }));

        Assert.Contains("already exists", exception.Message);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesSupplierSuccessfully()
    {
        var existing = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = "Old Name",
            City = "Lviv",
            IsActive = true
        };
        var repository = new FakeSupplierRepository();
        repository.Suppliers[existing.Id] = existing;
        var service = new SupplierService(repository);

        var result = await service.UpdateAsync(existing.Id, new UpdateSupplierDto
        {
            Name = "New Name",
            City = "Odesa",
            IsActive = false
        });

        Assert.NotNull(result);
        Assert.Equal("New Name", result!.Name);
        Assert.Equal("Odesa", result.City);
        Assert.False(result.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNullWhenNotFound()
    {
        var repository = new FakeSupplierRepository();
        var service = new SupplierService(repository);

        var result = await service.UpdateAsync(Guid.NewGuid(), new UpdateSupplierDto
        {
            Name = "Nonexistent"
        });

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_RejectsDuplicateName()
    {
        var existing1 = new Supplier { Id = Guid.NewGuid(), Name = "Supplier 1" };
        var existing2 = new Supplier { Id = Guid.NewGuid(), Name = "Supplier 2" };

        var repository = new FakeSupplierRepository();
        repository.Suppliers[existing1.Id] = existing1;
        repository.Suppliers[existing2.Id] = existing2;
        repository.ExistingNames.Add("supplier 2");

        var service = new SupplierService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(existing1.Id, new UpdateSupplierDto { Name = "Supplier 2" }));
    }

    [Fact]
    public async Task UpdateStatusAsync_TogglesIsActive()
    {
        var existing = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = "Supplier",
            IsActive = true
        };
        var repository = new FakeSupplierRepository();
        repository.Suppliers[existing.Id] = existing;
        var service = new SupplierService(repository);

        var result = await service.UpdateStatusAsync(existing.Id, false);

        Assert.NotNull(result);
        Assert.False(result!.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_SuccessWhenNoLinkedProductsOrShipments()
    {
        var existing = new Supplier { Id = Guid.NewGuid(), Name = "Clean Supplier" };
        var repository = new FakeSupplierRepository
        {
            HasProductsOrShipments = false
        };
        repository.Suppliers[existing.Id] = existing;
        var service = new SupplierService(repository);

        var deleted = await service.DeleteAsync(existing.Id);

        Assert.True(deleted);
        Assert.False(repository.Suppliers.ContainsKey(existing.Id));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseWhenNotFound()
    {
        var repository = new FakeSupplierRepository();
        var service = new SupplierService(repository);

        var deleted = await service.DeleteAsync(Guid.NewGuid());

        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteAsync_RejectsSupplierWithProductsOrShipments()
    {
        var existing = new Supplier { Id = Guid.NewGuid(), Name = "Busy Supplier" };
        var repository = new FakeSupplierRepository
        {
            HasProductsOrShipments = true
        };
        repository.Suppliers[existing.Id] = existing;
        var service = new SupplierService(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(existing.Id));

        Assert.Contains("associated products or shipments", exception.Message);
    }

    private sealed class FakeSupplierRepository : ISupplierRepository
    {
        public Dictionary<Guid, Supplier> Suppliers { get; } = new();
        public HashSet<string> ExistingNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Supplier? Created { get; private set; }
        public bool HasProductsOrShipments { get; init; }

        public Task<IReadOnlyList<Supplier>> GetAllAsync(
            bool onlyActive,
            CancellationToken cancellationToken = default)
        {
            var query = Suppliers.Values.AsEnumerable();
            if (onlyActive)
                query = query.Where(s => s.IsActive);
            return Task.FromResult<IReadOnlyList<Supplier>>(query.ToList());
        }

        public Task<Supplier?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Suppliers.GetValueOrDefault(id));

        public Task<Supplier> CreateAsync(
            Supplier supplier,
            CancellationToken cancellationToken = default)
        {
            Created = supplier;
            Suppliers[supplier.Id] = supplier;
            return Task.FromResult(supplier);
        }

        public Task<Supplier?> UpdateAsync(
            Supplier supplier,
            CancellationToken cancellationToken = default)
        {
            if (!Suppliers.ContainsKey(supplier.Id))
                return Task.FromResult<Supplier?>(null);

            Suppliers[supplier.Id] = supplier;
            return Task.FromResult<Supplier?>(supplier);
        }

        public Task<bool> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Suppliers.Remove(id));

        public Task<bool> HasProductsOrShipmentsAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HasProductsOrShipments);

        public Task<bool> ExistsByNameAsync(
            string name,
            Guid? excludeId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingNames.Contains(name));
    }
}
