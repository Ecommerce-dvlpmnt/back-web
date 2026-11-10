using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Exceptions;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using EcommerceBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Application.Services;

public sealed class OrderService(AppDbContext dbContext) : IOrderService
{
    public async Task<OrderResponse> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(request));

        var requestedQuantities = AggregateQuantities(request.Items);
        var productIds = requestedQuantities.Keys.Order().ToArray();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var products = await dbContext.Products
            .FromSqlInterpolated(
                $"SELECT * FROM \"Products\" WHERE \"Id\" = ANY ({productIds}) ORDER BY \"Id\" FOR UPDATE")
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var shortages = OrderStockValidator.GetShortages(requestedQuantities, products);
        if (shortages.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InsufficientStockException(shortages);
        }

        var now = DateTime.UtcNow;
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerFirstName = request.CustomerFirstName,
            CustomerLastName = request.CustomerLastName,
            CustomerPhone = request.CustomerPhone,
            CustomerComment = request.CustomerComment,
            DeliveryCityRef = request.DeliveryCityRef,
            DeliveryCityName = request.DeliveryCityName,
            DeliveryWarehouseRef = request.DeliveryWarehouseRef,
            DeliveryWarehouseName = request.DeliveryWarehouseName,
            PaymentMethod = request.PaymentMethod,
            OrderStatus = OrderStatus.New,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var (productId, quantity) in requestedQuantities)
        {
            var product = products[productId];
            product.Stock -= quantity;
            product.StockUpdatedAt = now;

            order.Items.Add(new OrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                Quantity = quantity,
                UnitPrice = product.Price,
                UnitDropPrice = product.DropPrice
            });

            order.TotalPrice += product.Price * quantity;
            order.TotalProfit += (product.Price - product.DropPrice) * quantity;
        }

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapToResponse(order);
    }

    public async Task<OrderResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(item => item.Items)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        return order is null ? null : MapToResponse(order);
    }

    private static Dictionary<Guid, int> AggregateQuantities(
        IReadOnlyList<CreateOrderItemRequest> items)
    {
        var totals = new Dictionary<Guid, long>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0)
                throw new ArgumentException("Item quantities must be greater than zero.", nameof(items));

            totals.TryGetValue(item.ProductId, out var currentQuantity);
            var totalQuantity = currentQuantity + item.Quantity;
            if (totalQuantity > int.MaxValue)
                throw new ArgumentException("The total quantity for a product is too large.", nameof(items));

            totals[item.ProductId] = totalQuantity;
        }

        return totals.ToDictionary(item => item.Key, item => (int)item.Value);
    }

    private static OrderResponse MapToResponse(Order order) =>
        new(
            order.Id,
            order.OrderNumber,
            order.OrderStatus,
            order.PaymentMethod,
            order.TotalPrice,
            order.Items
                .OrderBy(item => item.ProductId)
                .Select(item => new OrderItemResponse(
                    item.ProductId,
                    item.ProductNameSnapshot,
                    item.Quantity,
                    item.UnitPrice,
                    item.UnitPrice * item.Quantity))
                .ToArray());
}
