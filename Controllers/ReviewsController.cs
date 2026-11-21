using EcommerceBackend.Application.DTOs.Reviews;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/reviews")]
[Produces("application/json")]
public sealed class ReviewsController(IReviewService reviewService) : ControllerBase
{
    /// <summary>
    /// Оставить отзыв о товаре (публичный эндпоинт, регистрация не требуется).
    /// Новый отзыв автоматически получает статус Pending и отправляется на модерацию администратору.
    /// Защищен от спам-ботов скрытым полем Honeypot.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("order-limit")]
    [ProducesResponseType(typeof(ReviewDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
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
    /// Получить все одобренные (Approved) отзывы и сводную статистику (средний рейтинг, распределение звезд) по товару.
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
