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
    [ProducesResponseType(typeof(PagedResult<ProductDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetAll(
        [FromQuery] ProductQueryParameters query,
        CancellationToken cancellationToken)
    {
        query.OnlyActive ??= true;
        return Ok(await productService.GetAllAsync(query, cancellationToken));
    }

    /// <summary>Картка товару за ID для вітрини.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var product = await productService.GetByIdAsync(id, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    /// <summary>Картка товару за унікальним слагом (URL) для вітрини.</summary>
    [HttpGet("by-slug/{slug}")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var product = await productService.GetBySlugAsync(slug, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }
}
