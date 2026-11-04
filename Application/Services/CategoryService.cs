using System.Text.Json;
using EcommerceBackend.Application.DTOs.Categories;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;

namespace EcommerceBackend.Application.Services;

// Бизнес-слои категории: проверяет правила (уникальность slug, валидный JSON, запрет удаления непустых категорий)
// и превращает сущности БД в DTO (и наоборот). Controller общается только с этим классом.
public sealed class CategoryService(ICategoryRepository categoryRepository) : ICategoryService
{
    // Список всех категорий; onlyActive=true отдаёт только включённые (для клиентского каталога).
    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(
        bool? onlyActive,
        CancellationToken cancellationToken = default)
    {
        var categories = await categoryRepository.GetAllAsync(onlyActive, cancellationToken);
        return categories.Select(MapToDto).ToList();
    }

    // Ищем по Id; null -> Controller отдаст 404.
    public async Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await categoryRepository.GetByIdAsync(id, cancellationToken);
        return category is null ? null : MapToDto(category);
    }

    // Поисковая строка для витрины: /api/categories/by-slug/electronics
    public async Task<CategoryDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var category = await categoryRepository.GetBySlugAsync(slug, cancellationToken);
        return category is null ? null : MapToDto(category);
    }

    // Создание: проверяем свободный slug, собираем сущность, сохраняем, возвращаем DTO.
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

    // Полная замена категории. Сначала уникальность slug (кроме самого себя), потом ищем и перезаполняем поля.
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

    // Удаление: если у категории есть подкатегории или товары — запрет (409 через контроллер).
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await categoryRepository.HasChildrenOrProductsAsync(id, cancellationToken))
            throw new InvalidOperationException("Category has subcategories or products and cannot be deleted.");

        return await categoryRepository.DeleteAsync(id, cancellationToken);
    }

    // Проверка занятости slug (нужна и контроллеру для 409, и нам внутри).
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

    // FilterSchema — jsonb-текст: пусто -> "{}"; битый JSON -> ошибка 400 (аналогично Attributes у продуктов).
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

    // Entity -> DTO: ParentName берём из навигации, чтобы клиенту не требовался второй запрос.
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
