using EcommerceBackend.Application.DTOs.Reviews;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/reviews")]
[Produces("application/json")]
public sealed class ReviewsController(IReviewService reviewService) : ControllerBase
{
    /// <summary>
    /// Залишити відгук про товар (публічний ендпоінт, реєстрація не потрібна).
    /// Новий відгук автоматично отримує статус Pending і надсилається на модерацію адміністратору.
    /// Захищений від спам-ботів прихованим полем Honeypot.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDto>> Create(
        [FromBody] CreateReviewDto request,
        CancellationToken cancellationToken)
    {
        var created = await reviewService.CreateReviewAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetByProductId),
            new { productId = created.ProductId },
            created);
    }

    /// <summary>
    /// Отримати всі схвалені (Approved) відгуки та зведену статистику (середній рейтинг, розподіл зірок) за товаром.
    /// </summary>
    [HttpGet("product/{productId:guid}")]
    [ProducesResponseType(typeof(ProductReviewsSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProductReviewsSummaryDto>> GetByProductId(
        Guid productId,
        CancellationToken cancellationToken)
    {
        var summary = await reviewService.GetProductReviewsAsync(productId, cancellationToken);
        return Ok(summary);
    }
}
