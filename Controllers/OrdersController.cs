using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Exceptions;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    /// <summary>Оформити замовлення з кошика (публічний ендпоінт вітрини).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(StockConflictResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await orderService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetByPublicId), new { publicId = order.PublicId }, order);
    }

    /// <summary>Отримати замовлення за публічним кодом (для сторінки "Дякуємо за замовлення" на вітрині).</summary>
    [HttpGet("by-public-id/{publicId}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetByPublicId(
        string publicId,
        CancellationToken cancellationToken)
    {
        var order = await orderService.GetByPublicIdAsync(publicId, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Скасувати гостьове замовлення за публічним кодом (лише статус New і не оплачене).</summary>
    [HttpPost("by-public-id/{publicId}/cancel")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> CancelByPublicId(
        string publicId,
        CancellationToken cancellationToken)
    {
        var order = await orderService.CancelByPublicIdAsync(publicId, cancellationToken);
        return Ok(order);
    }
}
