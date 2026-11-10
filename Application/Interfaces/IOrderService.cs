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
}
