using System.Text.Json;
using EcommerceBackend.Application.DTOs.Categories;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;

namespace EcommerceBackend.Application.Services;

public sealed class CategoryService(ICategoryRepository categoryRepository) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(
        bool? onlyActive,
        CancellationToken cancellationToken = default)
    {
        var categories = await categoryRepository.GetAllAsync(onlyActive, cancellationToken);
        return categories.Select(MapToDto).ToList();
    }

    public async Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await categoryRepository.GetByIdAsync(id, cancellationToken);
        return category is null ? null : MapToDto(category);
    }

    public async Task<CategoryDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var category = await categoryRepository.GetBySlugAsync(slug, cancellationToken);
        return category is null ? null : MapToDto(category);
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryDto request, CancellationToken cancellationToken = default)
    {
        await EnsureUniqueAsync(request.Slug, null, cancellationToken);

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Slug = request.Slug,
            ParentId = request.ParentId,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            FilterSchema = NormalizeFilterSchema(request.FilterSchema),
            MetaTitle = request.MetaTitle,
            MetaDescription = request.MetaDescription
        };

        return MapToDto(await categoryRepository.CreateAsync(category, cancellationToken));
    }

    public async Task<CategoryDto?> UpdateAsync(
        Guid id,
        UpdateCategoryDto request,
        CancellationToken cancellationToken = default)
    {
        await EnsureUniqueAsync(request.Slug, id, cancellationToken);
        var category = await categoryRepository.GetByIdAsync(id, cancellationToken);
        if (category is null)
            return null;

        category.Name = request.Name;
        category.Slug = request.Slug;
        category.ParentId = request.ParentId;
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        category.FilterSchema = NormalizeFilterSchema(request.FilterSchema);
        category.MetaTitle = request.MetaTitle;
        category.MetaDescription = request.MetaDescription;

        var updated = await categoryRepository.UpdateAsync(category, cancellationToken);
        return updated is null ? null : MapToDto(updated);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await categoryRepository.HasChildrenOrProductsAsync(id, cancellationToken))
            throw new InvalidOperationException("Category has subcategories or products and cannot be deleted.");

        return await categoryRepository.DeleteAsync(id, cancellationToken);
    }

    public Task<bool> ExistsBySlugAsync(
        string slug,
        Guid? excludeId = null,
        CancellationToken cancellationToken = default) =>
        categoryRepository.ExistsBySlugAsync(slug, excludeId, cancellationToken);

    private async Task EnsureUniqueAsync(string slug, Guid? excludeId, CancellationToken cancellationToken)
    {
        if (await ExistsBySlugAsync(slug, excludeId, cancellationToken))
            throw new InvalidOperationException("A category with this slug already exists.");
    }

    private static string NormalizeFilterSchema(string? filterSchema)
    {
        if (string.IsNullOrWhiteSpace(filterSchema))
            return "{}";

        try
        {
            using var _ = JsonDocument.Parse(filterSchema);
            return filterSchema;
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("FilterSchema must contain valid JSON.", nameof(filterSchema), exception);
        }
    }

    private static CategoryDto MapToDto(Category category) => new()
    {
        Id = category.Id,
        ParentId = category.ParentId,
        ParentName = category.Parent?.Name,
        Name = category.Name,
        Slug = category.Slug,
        SortOrder = category.SortOrder,
        IsActive = category.IsActive,
        FilterSchema = category.FilterSchema,
        MetaTitle = category.MetaTitle,
        MetaDescription = category.MetaDescription
    };
}
