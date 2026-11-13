using EcommerceBackend.Application.DTOs.Orders;

namespace EcommerceBackend.Application.Interfaces;

public interface IOrderService
{
    Task<OrderResponse> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default);

    Task<OrderResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<AdminOrderDetailResponse?> GetAdminOrderDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<OrderResponse?> GetByPublicIdAsync(
        string publicId,
        CancellationToken cancellationToken = default);

    Task<OrderResponse> CancelByPublicIdAsync(
        string publicId,
        CancellationToken cancellationToken = default);

    Task<PagedOrderListResponse> GetAllAsync(
        OrderListQuery query,
        CancellationToken cancellationToken = default);

    Task<OrderResponse> UpdateStatusAsync(
        Guid id,
        UpdateOrderStatusRequest request,
        Guid? adminId = null,
        CancellationToken cancellationToken = default);

    Task<AdminOrderDetailResponse?> UpdateCommentAsync(
        Guid id,
        string? comment,
        CancellationToken cancellationToken = default);
}
