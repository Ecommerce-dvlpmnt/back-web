using EcommerceBackend.Application.DTOs.Categories;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Domain.Entities;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class CategoryServiceTests
{
    [Fact]
    public async Task CreateAsync_DefaultsMissingFilterSchemaToEmptyObject()
    {
        var repository = new FakeCategoryRepository();
        var service = new CategoryService(repository);

        var result = await service.CreateAsync(new CreateCategoryDto
        {
            Name = "Decor",
            Slug = "decor"
        });

        Assert.NotNull(repository.Created);
        Assert.Equal("{}", repository.Created!.FilterSchema);
        Assert.Equal(repository.Created.Id, result.Id);
    }

    [Fact]
    public async Task CreateAsync_RejectsInvalidFilterSchema()
    {
        var service = new CategoryService(new FakeCategoryRepository());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new CreateCategoryDto
        {
            Name = "Decor",
            Slug = "decor",
            FilterSchema = "{"
        }));

        Assert.Contains("valid JSON", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateSlug()
    {
        var repository = new FakeCategoryRepository();
        repository.ExistingSlugs.Add("decor");
        var service = new CategoryService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(new CreateCategoryDto
        {
            Name = "Decor",
            Slug = "decor"
        }));
    }

    [Fact]
    public async Task DeleteAsync_RejectsCategoryWithChildrenOrProducts()
    {
        var repository = new FakeCategoryRepository
        {
            HasChildrenOrProducts = true
        };
        var service = new CategoryService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(Guid.NewGuid()));
    }

    private sealed class FakeCategoryRepository : ICategoryRepository
    {
        public HashSet<string> ExistingSlugs { get; } = new(StringComparer.Ordinal);
        public Category? Created { get; private set; }
        public bool HasChildrenOrProducts { get; init; }

        public Task<IReadOnlyList<Category>> GetAllAsync(
            bool? onlyActive,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Category>>(new List<Category>());

        public Task<Category?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Category?>(null);

        public Task<Category?> GetBySlugAsync(
            string slug,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Category?>(null);

        public Task<Category> CreateAsync(
            Category category,
            CancellationToken cancellationToken = default)
        {
            Created = category;
            return Task.FromResult(category);
        }

        public Task<Category?> UpdateAsync(
            Category category,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Category?>(category);

        public Task<bool> DeleteAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ExistsBySlugAsync(
            string slug,
            Guid? excludeId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExistingSlugs.Contains(slug));

        public Task<bool> HasChildrenOrProductsAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HasChildrenOrProducts);
    }
}
