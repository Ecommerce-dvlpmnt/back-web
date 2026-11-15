using EcommerceBackend.Application.DTOs.Suppliers;
using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers.Admin;

[ApiController]
[Route("api/admin/suppliers")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class AdminSuppliersController(ISupplierService supplierService) : ControllerBase
{
    /// <summary>Список всех поставщиков для администратора.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> GetAll(
        [FromQuery] bool onlyActive = false,
        CancellationToken cancellationToken = default)
    {
        return Ok(await supplierService.GetAllAsync(onlyActive, cancellationToken));
    }

    /// <summary>Информация о поставщике по ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var supplier = await supplierService.GetByIdAsync(id, cancellationToken);
        return supplier is null ? NotFound() : Ok(supplier);
    }

    /// <summary>Создание нового поставщика товаров.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierDto>> Create(
        [FromBody] CreateSupplierDto request,
        CancellationToken cancellationToken)
    {
        var supplier = await supplierService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, supplier);
    }
}
