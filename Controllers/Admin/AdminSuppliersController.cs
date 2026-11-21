using EcommerceBackend.Application.DTOs.Suppliers;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers.Admin;

[ApiController]
[Route("api/admin/suppliers")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class AdminSuppliersController(ISupplierService supplierService) : ControllerBase
{
    /// <summary>Список всех поставщиков для администратора с фильтрацией по статусу активности.</summary>
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
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierDto>> Create(
        [FromBody] CreateSupplierDto request,
        CancellationToken cancellationToken)
    {
        var supplier = await supplierService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = supplier.Id }, supplier);
    }

    /// <summary>Оновлення даних постачальника.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SupplierDto>> Update(
        Guid id,
        [FromBody] UpdateSupplierDto request,
        CancellationToken cancellationToken)
    {
        var supplier = await supplierService.UpdateAsync(id, request, cancellationToken);
        return supplier is null ? NotFound() : Ok(supplier);
    }

    /// <summary>Швидке перемикання статусу активності постачальника (активний / неактивний).</summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(SupplierDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateSupplierStatusDto request,
        CancellationToken cancellationToken)
    {
        var supplier = await supplierService.UpdateStatusAsync(id, request.IsActive, cancellationToken);
        return supplier is null ? NotFound() : Ok(supplier);
    }

    /// <summary>Видалення постачальника (якщо до нього не прив'язані товари або відправлення).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await supplierService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
