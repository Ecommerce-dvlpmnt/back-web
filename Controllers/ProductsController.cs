using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/products")]
[Produces("application/json")]
public sealed class ProductsController(IProductService productService) : ControllerBase
{
    /// <summary>Каталог активних товарів для вітрини магазину з пагінацією, фільтрацією та пошуком.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PublicProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<PublicProductDto>>> GetAll(
        [FromQuery] PublicProductQueryParameters query,
        CancellationToken cancellationToken)
    {
        var parameters = new ProductQueryParameters
        {
            CategoryId = query.CategoryId,
            Search = query.Search,
            SortBy = query.SortBy,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            OnlyActive = true
        };
        return Ok(await productService.GetPublicAllAsync(parameters, cancellationToken));
    }

    /// <summary>Картка активного товару за ID для вітрини.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PublicProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicProductDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var product = await productService.GetPublicByIdAsync(id, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    /// <summary>Картка активного товару за унікальним слагом (URL) для вітрини.</summary>
    [HttpGet("by-slug/{slug}")]
    [ProducesResponseType(typeof(PublicProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicProductDto>> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var product = await productService.GetPublicBySlugAsync(slug, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }
}
