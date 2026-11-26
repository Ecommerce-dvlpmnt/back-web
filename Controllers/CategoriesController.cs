using EcommerceBackend.Application.DTOs.Categories;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public sealed class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    /// <summary>Отримати список активних категорій для вітрини каталогу.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<CategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetAll(
        [FromQuery] bool? onlyActive,
        CancellationToken cancellationToken)
    {
        var categories = await categoryService.GetAllAsync(onlyActive ?? true, cancellationToken);
        return Ok(categories.Select(WithPublicSeoFallbacks).ToList());
    }

    /// <summary>Отримати категорію за ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var category = await categoryService.GetByIdAsync(id, cancellationToken);
        return category is null ? NotFound() : Ok(WithPublicSeoFallbacks(category));
    }

    /// <summary>Отримати категорію за унікальним слагом (URL).</summary>
    [HttpGet("by-slug/{slug}")]
    [ProducesResponseType(typeof(CategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDto>> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var category = await categoryService.GetBySlugAsync(slug, cancellationToken);
        return category is null ? NotFound() : Ok(WithPublicSeoFallbacks(category));
    }

    private static CategoryDto WithPublicSeoFallbacks(CategoryDto dto)
    {
        dto.MetaTitle = SeoMetaBuilder.BuildCategoryMetaTitle(dto.Name, dto.MetaTitle);
        dto.MetaDescription = SeoMetaBuilder.BuildCategoryMetaDescription(dto.Name, dto.MetaDescription);
        return dto;
    }
}
