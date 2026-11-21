using EcommerceBackend.Application.DTOs.Reviews;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Enums;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers.Admin;

[ApiController]
[Route("api/admin/reviews")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class AdminReviewsController(IReviewService reviewService) : ControllerBase
{
    /// <summary>
    /// Список відгуків для модерації (всі статуси: Pending, Approved, Rejected) з фільтрацією та пагінацією.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ReviewDto>>> GetAll(
        [FromQuery] ReviewStatus? status,
        [FromQuery] Guid? productId,
        [FromQuery] Guid? categoryId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var reviews = await reviewService.GetAdminReviewsAsync(
            status,
            productId,
            categoryId,
            page,
            pageSize,
            cancellationToken);

        return Ok(reviews);
    }

    /// <summary>
    /// Перегляд відгуку за ID з усіма деталями та метаданими.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var review = await reviewService.GetByIdAsync(id, cancellationToken);
        return review is null ? NotFound() : Ok(review);
    }

    /// <summary>
    /// Модерація відгуку: схвалення (Approved) для публікації на сайті або відхилення (Rejected) з коментарем.
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDto>> UpdateStatus(
        Guid id,
        [FromBody] UpdateReviewStatusDto request,
        CancellationToken cancellationToken)
    {
        var updated = await reviewService.UpdateStatusAsync(id, request, cancellationToken);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>
    /// Видалення відгуку (для спаму або некоректних записів).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var deleted = await reviewService.DeleteAsync(id, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
