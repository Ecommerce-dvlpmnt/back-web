using System.Security.Claims;
using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Exceptions;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/orders")]
[Produces("application/json")]
public sealed class OrdersController(IOrderService orderService) : ControllerBase
{
    /// <summary>Получить список заказов с фильтрацией и пагинацией (для админки и менеджеров).</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedOrderListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedOrderListResponse>> GetAll(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var response = await orderService.GetAllAsync(query, cancellationToken);
        return Ok(response);
    }

    /// <summary>Получить заказ по ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var order = await orderService.GetByIdAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Получить заказ по публичному коду (для страницы "Спасибо за заказ" на витрине).</summary>
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

    /// <summary>Оформить заказ из корзины (публичный эндпоинт витрины).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(StockConflictResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderResponse>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await orderService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = order.Id }, order);
    }

    /// <summary>Изменить статус заказа (для админки и менеджеров).</summary>
    [HttpPatch("{id:guid}/status")]
    [Authorize]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(
        Guid id,
        [FromBody] UpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        Guid? adminId = null;
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(claim, out var parsed))
        {
            adminId = parsed;
        }

        var order = await orderService.UpdateStatusAsync(id, request, adminId, cancellationToken);
        return Ok(order);
    }
}
