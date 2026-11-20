using EcommerceBackend.Application.DTOs.Admin;
using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class ProductServiceTests
{
    [Fact]
    public async Task GetPublicAllAsync_ForcesOnlyActiveAndReturnsPublicDtoWithoutWholesaleData()
    {
        var repo = new FakeProductRepository();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            Sku = "TEST-1",
            Name = "Test Lamp",
            Slug = "test-lamp",
            Price = 500m,
            DropPrice = 250m,
            Stock = 5,
            IsActive = true
        };
        repo.Products[product.Id] = product;

        var service = new ProductService(repo, new FakeImageStorageService(), new FakeAuditLogService());

        var query = new ProductQueryParameters
        {
            OnlyActive = false // Client tries to bypass active filter
        };

        var result = await service.GetPublicAllAsync(query);

        Assert.True(query.OnlyActive);
        var item = Assert.Single(result.Items);
        Assert.Equal("Test Lamp", item.Name);
        Assert.Equal(500m, item.Price);
        Assert.True(item.IsActive);
        // Ensure PublicProductDto does not leak wholesale info (properties do not exist on PublicProductDto)
        Assert.DoesNotContain(typeof(PublicProductDto).GetProperties(), p => p.Name == "DropPrice");
        Assert.DoesNotContain(typeof(PublicProductDto).GetProperties(), p => p.Name == "SupplierId");
    }

    [Fact]
    public async Task GetPublicByIdAsync_ReturnsNullWhenProductIsInactive()
    {
        var repo = new FakeProductRepository();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "INACTIVE-1",
            Name = "Inactive Lamp",
            Slug = "inactive-lamp",
            Price = 300m,
            DropPrice = 150m,
            Stock = 5,
            IsActive = false
        };
        repo.Products[product.Id] = product;

        var service = new ProductService(repo, new FakeImageStorageService(), new FakeAuditLogService());

        var result = await service.GetPublicByIdAsync(product.Id);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetPublicBySlugAsync_ReturnsNullWhenProductIsInactive()
    {
        var repo = new FakeProductRepository();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "INACTIVE-2",
            Name = "Inactive Lamp 2",
            Slug = "inactive-lamp-2",
            Price = 300m,
            DropPrice = 150m,
            Stock = 5,
            IsActive = false
        };
        repo.Products[product.Id] = product;

        var service = new ProductService(repo, new FakeImageStorageService(), new FakeAuditLogService());

        var result = await service.GetPublicBySlugAsync("inactive-lamp-2");

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateStatusAsync_TogglesIsActiveAndLogsAudit()
    {
        var repo = new FakeProductRepository();
        var audit = new FakeAuditLogService();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "STATUS-1",
            Name = "Status Product",
            Slug = "status-product",
            IsActive = true
        };
        repo.Products[product.Id] = product;

        var service = new ProductService(repo, new FakeImageStorageService(), audit);

        var deactivated = await service.UpdateStatusAsync(product.Id, false);

        Assert.NotNull(deactivated);
        Assert.False(deactivated!.IsActive);
        Assert.Contains(audit.Logs, l => l.Action == AuditLogActions.ProductDeactivated);

        var activated = await service.UpdateStatusAsync(product.Id, true);

        Assert.NotNull(activated);
        Assert.True(activated!.IsActive);
        Assert.Contains(audit.Logs, l => l.Action == AuditLogActions.ProductActivated);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenProductHasOrders()
    {
        var repo = new FakeProductRepository
        {
            HasOrders = true
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "ORDERED-1",
            Name = "Ordered Product",
            Slug = "ordered-product",
            IsActive = false
        };
        repo.Products[product.Id] = product;

        var service = new ProductService(repo, new FakeImageStorageService(), new FakeAuditLogService());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(product.Id));

        Assert.Contains("Неможливо видалити товар, за яким вже є оформлені замовлення", ex.Message);
        Assert.True(repo.Products.ContainsKey(product.Id));
    }

    [Fact]
    public async Task DeleteAsync_DeletesProductWhenNoOrdersExist()
    {
        var repo = new FakeProductRepository
        {
            HasOrders = false
        };
        var audit = new FakeAuditLogService();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "FRESH-1",
            Name = "Fresh Product",
            Slug = "fresh-product",
            IsActive = false
        };
        repo.Products[product.Id] = product;

        var service = new ProductService(repo, new FakeImageStorageService(), audit);

        var result = await service.DeleteAsync(product.Id);

        Assert.True(result);
        Assert.False(repo.Products.ContainsKey(product.Id));
        Assert.Contains(audit.Logs, l => l.Action == AuditLogActions.ProductDeleted);
    }

    [Fact]
    public async Task CreateAsync_RejectsDropPriceGreaterThanPrice()
    {
        var repo = new FakeProductRepository();
        var service = new ProductService(repo, new FakeImageStorageService(), new FakeAuditLogService());

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateProductDto
        {
            Sku = "BAD-PRICE-1",
            Name = "Bad Price Product",
            Slug = "bad-price-product",
            Price = 200m,
            DropPrice = 300m,
            Stock = 10
        }));

        Assert.Contains("DropPrice", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsNegativeDropPriceOrWeight()
    {
        var repo = new FakeProductRepository();
        var service = new ProductService(repo, new FakeImageStorageService(), new FakeAuditLogService());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateProductDto
        {
            Sku = "NEG-DROP-1",
            Name = "Neg Drop Product",
            Slug = "neg-drop-product",
            Price = 200m,
            DropPrice = -10m,
            Stock = 10
        }));

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateProductDto
        {
            Sku = "NEG-WEIGHT-1",
            Name = "Neg Weight Product",
            Slug = "neg-weight-product",
            Price = 200m,
            DropPrice = 100m,
            WeightKg = -0.5m,
            Stock = 10
        }));
    }

    private sealed class FakeProductRepository : IProductRepository
    {
        public Dictionary<Guid, Product> Products { get; } = new();
        public bool HasOrders { get; init; }

        public Task<PagedResult<Product>> GetAllAsync(
            ProductQueryParameters query,
            CancellationToken cancellationToken = default)
        {
            var items = Products.Values.AsEnumerable();
            if (query.OnlyActive == true)
            {
                items = items.Where(p => p.IsActive);
            }

            var list = items.ToList();
            return Task.FromResult(new PagedResult<Product>
            {
                Items = list,
                TotalCount = list.Count,
                PageNumber = 1,
                PageSize = 10,
                TotalPages = 1
            });
        }

        public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.GetValueOrDefault(id));

        public Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.Values.FirstOrDefault(p => p.Slug == slug));

        public Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default)
        {
            Products[product.Id] = product;
            return Task.FromResult(product);
        }

        public Task<Product?> UpdateAsync(Product product, CancellationToken cancellationToken = default)
        {
            if (!Products.ContainsKey(product.Id))
            {
                return Task.FromResult<Product?>(null);
            }

            Products[product.Id] = product;
            return Task.FromResult<Product?>(product);
        }

        public Task<Product?> UpdateImageAsync(
            Guid productId,
            string? mainImageUrl,
            ProductImage? image,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.GetValueOrDefault(productId));

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.Remove(id));

        public Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (!Products.TryGetValue(id, out var product))
            {
                return Task.FromResult(false);
            }

            product.IsActive = false;
            return Task.FromResult(true);
        }

        public Task<bool> HasOrdersAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(HasOrders);

        public Task<bool> UpdateStockAsync(Guid id, int stock, CancellationToken cancellationToken = default)
        {
            if (!Products.TryGetValue(id, out var product))
            {
                return Task.FromResult(false);
            }

            product.Stock = stock;
            return Task.FromResult(true);
        }

        public Task<bool> ExistsBySkuOrSlugAsync(
            string sku,
            string slug,
            Guid? excludeId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Products.Values.Any(p =>
                (p.Sku == sku || p.Slug == slug) &&
                (!excludeId.HasValue || p.Id != excludeId.Value)));
    }

    private sealed class FakeImageStorageService : IImageStorageService
    {
        public Task<string> UploadImageAsync(
            IFormFile file,
            string folderName,
            CancellationToken cancellationToken) =>
            Task.FromResult("https://storage.example.com/test.jpg");
    }

    private sealed class FakeAuditLogService : IAuditLogService
    {
        public List<AdminAuditLog> Logs { get; } = new();

        public Task LogAsync(
            string action,
            string entityType,
            string entityId,
            object details,
            CancellationToken cancellationToken = default)
        {
            Logs.Add(new AdminAuditLog
            {
                Id = Guid.NewGuid(),
                AdminUsername = "admin",
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = System.Text.Json.JsonSerializer.Serialize(details),
                CreatedAt = DateTime.UtcNow
            });
            return Task.CompletedTask;
        }

        public Task<PagedResult<AdminAuditLogDto>> GetPagedAsync(
            AuditLogQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<AdminAuditLogDto>());

        public Task<IReadOnlyList<AdminAuditLog>> GetLogsAsync(
            string? entityType = null,
            string? entityId = null,
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdminAuditLog>>(Logs);
    }
}
