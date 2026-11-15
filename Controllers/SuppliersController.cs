using EcommerceBackend.Application.DTOs.Suppliers;
using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/suppliers")]
[Produces("application/json")]
public sealed class SuppliersController(ISupplierService supplierService) : ControllerBase
{
    /// <summary>Список активных поставщиков для фильтров каталога.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> GetAll(
        [FromQuery] bool onlyActive = true,
        CancellationToken cancellationToken = default)
    {
        return Ok(await supplierService.GetAllAsync(onlyActive, cancellationToken));
    }

    /// <summary>Информация о поставщике по ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var supplier = await supplierService.GetByIdAsync(id, cancellationToken);
        return supplier is null ? NotFound() : Ok(supplier);
    }
}
