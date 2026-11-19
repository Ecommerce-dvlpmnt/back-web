using System.Security.Claims;
using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EcommerceBackend.Controllers.Admin;

[ApiController]
[Route("api/admin/orders")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class AdminOrdersController(IOrderService orderService) : ControllerBase
{
    /// <summary>Список всех заказов с пагинацией, фильтрацией по статусу заказа и диапазону дат (From, To).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedOrderListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedOrderListResponse>> GetAll(
        [FromQuery] OrderListQuery query,
        CancellationToken cancellationToken)
    {
        var response = await orderService.GetAllAsync(query, cancellationToken);
        return Ok(response);
    }

    /// <summary>Повна інформація про замовлення (контакти, доставка, товари, собівартість/дроп-ціна, прибуток, історія зміни статусів).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AdminOrderDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminOrderDetailResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var order = await orderService.GetAdminOrderDetailAsync(id, cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Зміна статусу із записом в історію (OrderStatusHistory) та поверненням на склад при скасуванні або поверненні.</summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

    /// <summary>Оновлення внутрішнього коментаря адміністратора до замовлення.</summary>
    [HttpPatch("{id:guid}/comment")]
    [ProducesResponseType(typeof(AdminOrderDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminOrderDetailResponse>> UpdateComment(
        Guid id,
        [FromBody] UpdateOrderCommentRequest request,
        CancellationToken cancellationToken)
    {
        var order = await orderService.UpdateCommentAsync(id, request.GetComment(), cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    /// <summary>Підтвердження оплати замовлення адміністратором та обробка реферальної винагороди.</summary>
    [HttpPost("{id:guid}/confirm-payment")]
    [ProducesResponseType(typeof(OrderPaymentConfirmationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderPaymentConfirmationResponse>> ConfirmPayment(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConfirmOrderPaymentRequest? request,
        CancellationToken cancellationToken)
    {
        Guid? adminId = null;
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(claim, out var parsed))
        {
            adminId = parsed;
        }

        var result = await orderService.ConfirmPaymentAsync(id, request, adminId, cancellationToken);
        return Ok(result);
    }
}
