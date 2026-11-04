using EcommerceBackend.Application.DTOs.Categories;

namespace EcommerceBackend.Application.Interfaces;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(bool? onlyActive, CancellationToken cancellationToken = default);
    Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CategoryDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<CategoryDto> CreateAsync(CreateCategoryDto request, CancellationToken cancellationToken = default);
    Task<CategoryDto?> UpdateAsync(Guid id, UpdateCategoryDto request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsBySlugAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
