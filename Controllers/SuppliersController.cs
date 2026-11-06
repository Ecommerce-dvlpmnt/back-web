using EcommerceBackend.Application.DTOs.Suppliers;
using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/suppliers")]
[Produces("application/json")]
public sealed class SuppliersController(ISupplierService supplierService) : ControllerBase
{
    /// <summary>Returns suppliers ordered by name, by default only the active ones (for dropdown lists).</summary>
    /// <param name="onlyActive">When true (default), only active suppliers are returned.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SupplierDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SupplierDto>>> GetAll(
        [FromQuery] bool onlyActive = true,
        CancellationToken cancellationToken = default)
    {
        return Ok(await supplierService.GetAllAsync(onlyActive, cancellationToken));
    }

    /// <summary>Returns a supplier by its Guid identifier.</summary>
    /// <param name="id">Supplier identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
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

    /// <summary>Creates a supplier. Supplier names must be unique (case-insensitive).</summary>
    /// <param name="request">Supplier fields.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    [HttpPost]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierDto>> Create(
        [FromBody] CreateSupplierDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var supplier = await supplierService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, supplier);
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = "Supplier conflict", Detail = exception.Message });
        }
    }
}
