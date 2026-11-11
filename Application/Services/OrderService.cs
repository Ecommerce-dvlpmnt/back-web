using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Exceptions;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
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

        // Resolving names flexibly for storefronts
        var rawName = (request.FullName ?? request.CustomerName ?? request.Name ?? "").Trim();
        var firstName = request.CustomerFirstName?.Trim() ?? "";
        var lastName = request.CustomerLastName?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(firstName) && !string.IsNullOrWhiteSpace(rawName))
        {
            var parts = rawName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            firstName = parts[0];
            lastName = parts.Length > 1 ? parts[1] : parts[0];
        }
        else if (!string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
        {
            var parts = firstName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                firstName = parts[0];
                lastName = parts[1];
            }
            else
            {
                lastName = firstName;
            }
        }

        if (string.IsNullOrWhiteSpace(firstName)) firstName = "Покупець";
        if (string.IsNullOrWhiteSpace(lastName)) lastName = firstName;

        var phone = (request.CustomerPhone ?? request.Phone ?? "").Trim();
        if (string.IsNullOrWhiteSpace(phone))
            throw new ArgumentException("Номер телефону є обов'язковим для оформлення замовлення.", nameof(request));

        var email = (request.CustomerEmail ?? request.Email)?.Trim();
        var cityName = (request.DeliveryCityName ?? request.City)?.Trim();
        var warehouseName = (request.DeliveryWarehouseName ?? request.Warehouse ?? request.Address)?.Trim();

        if (!string.IsNullOrWhiteSpace(request.DeliveryType) && !string.IsNullOrWhiteSpace(warehouseName)
            && !warehouseName.Contains(request.DeliveryType, StringComparison.OrdinalIgnoreCase))
        {
            warehouseName = $"{request.DeliveryType}: {warehouseName}";
        }

        var comment = (request.CustomerComment ?? request.Comment)?.Trim();
        var now = DateTime.UtcNow;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            PublicId = Guid.NewGuid().ToString("N"),
            CustomerFirstName = firstName,
            CustomerLastName = lastName,
            CustomerPhone = phone,
            CustomerEmail = email,
            CustomerComment = comment,
            DeliveryCityRef = request.DeliveryCityRef,
            DeliveryCityName = cityName,
            DeliveryWarehouseRef = request.DeliveryWarehouseRef,
            DeliveryWarehouseName = warehouseName,
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

    public async Task<AdminOrderDetailResponse?> GetAdminOrderDetailAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
                .ThenInclude(h => h.ChangedByAdmin)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        return order is null ? null : MapToAdminDetailResponse(order);
    }

    public async Task<OrderResponse?> GetByPublicIdAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(item => item.Items)
            .FirstOrDefaultAsync(item => item.PublicId == publicId, cancellationToken);

        return order is null ? null : MapToResponse(order);
    }

    public async Task<PagedOrderListResponse> GetAllAsync(
        OrderListQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = query.PageNumber <= 0 ? 1 : query.PageNumber;
        var pageSize = query.PageSize <= 0 ? 20 : (query.PageSize > 100 ? 100 : query.PageSize);

        var dbQuery = dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .AsQueryable();

        if (query.Status.HasValue)
        {
            dbQuery = dbQuery.Where(o => o.OrderStatus == query.Status.Value);
        }

        if (query.From.HasValue)
        {
            var fromUtc = query.From.Value.Kind == DateTimeKind.Utc
                ? query.From.Value
                : DateTime.SpecifyKind(query.From.Value, DateTimeKind.Utc);
            dbQuery = dbQuery.Where(o => o.CreatedAt >= fromUtc);
        }

        if (query.To.HasValue)
        {
            var toUtc = query.To.Value.Kind == DateTimeKind.Utc
                ? query.To.Value
                : DateTime.SpecifyKind(query.To.Value, DateTimeKind.Utc);
            dbQuery = dbQuery.Where(o => o.CreatedAt <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            dbQuery = dbQuery.Where(o =>
                o.CustomerPhone.Contains(s) ||
                o.CustomerFirstName.ToLower().Contains(s) ||
                o.CustomerLastName.ToLower().Contains(s) ||
                o.PublicId.ToLower().Contains(s) ||
                o.OrderNumber.ToString().Contains(s) ||
                (o.CustomerEmail != null && o.CustomerEmail.ToLower().Contains(s)));
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var items = await dbQuery
            .OrderByDescending(o => o.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PagedOrderListResponse(
            Items: items.Select(MapToResponse).ToList(),
            TotalCount: totalCount,
            PageNumber: pageNumber,
            PageSize: pageSize,
            TotalPages: totalPages);
    }

    public async Task<OrderResponse> UpdateStatusAsync(
        Guid id,
        UpdateOrderStatusRequest request,
        Guid? adminId = null,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            throw new KeyNotFoundException($"Замовлення з ID '{id}' не знайдено.");
        }

        var oldStatus = order.OrderStatus;
        var newStatus = request.Status;

        if (oldStatus == newStatus)
        {
            return MapToResponse(order);
        }

        // Return stock back if cancelled
        if (newStatus == OrderStatus.Cancelled
            && oldStatus != OrderStatus.Cancelled
            && oldStatus != OrderStatus.Refused
            && oldStatus != OrderStatus.Returned)
        {
            var productIds = order.Items.Select(i => i.ProductId).ToArray();
            var products = await dbContext.Products
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            foreach (var item in order.Items)
            {
                if (products.TryGetValue(item.ProductId, out var product))
                {
                    product.Stock += item.Quantity;
                    product.StockUpdatedAt = DateTime.UtcNow;
                }
            }
        }

        order.OrderStatus = newStatus;
        order.UpdatedAt = DateTime.UtcNow;

        dbContext.OrderStatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            ChangedByAdminId = adminId,
            Comment = request.Comment,
            CreatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapToResponse(order);
    }

    public async Task<AdminOrderDetailResponse?> UpdateCommentAsync(
        Guid id,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
                .ThenInclude(h => h.ChangedByAdmin)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null) return null;

        order.AdminComment = comment;
        order.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapToAdminDetailResponse(order);
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

    private static OrderResponse MapToResponse(Order order)
    {
        var fullName = $"{order.CustomerFirstName} {order.CustomerLastName}".Trim();
        if (string.IsNullOrWhiteSpace(fullName)) fullName = order.CustomerFirstName;

        return new OrderResponse(
            Id: order.Id,
            OrderNumber: order.OrderNumber,
            PublicId: order.PublicId,
            CustomerFirstName: order.CustomerFirstName,
            CustomerLastName: order.CustomerLastName,
            CustomerFullName: fullName,
            CustomerPhone: order.CustomerPhone,
            CustomerEmail: order.CustomerEmail,
            CustomerComment: order.CustomerComment,
            DeliveryCityName: order.DeliveryCityName,
            DeliveryWarehouseName: order.DeliveryWarehouseName,
            OrderStatus: order.OrderStatus,
            PaymentMethod: order.PaymentMethod,
            PaymentStatus: order.PaymentStatus,
            TotalPrice: order.TotalPrice,
            CreatedAt: order.CreatedAt,
            Items: order.Items
                .OrderBy(item => item.ProductId)
                .Select(item => new OrderItemResponse(
                    item.ProductId,
                    item.ProductNameSnapshot,
                    item.Quantity,
                    item.UnitPrice,
                    item.UnitPrice * item.Quantity))
                .ToArray());
    }

    private static AdminOrderDetailResponse MapToAdminDetailResponse(Order order)
    {
        var fullName = $"{order.CustomerFirstName} {order.CustomerLastName}".Trim();
        if (string.IsNullOrWhiteSpace(fullName)) fullName = order.CustomerFirstName;

        return new AdminOrderDetailResponse(
            Id: order.Id,
            OrderNumber: order.OrderNumber,
            PublicId: order.PublicId,
            CustomerFirstName: order.CustomerFirstName,
            CustomerLastName: order.CustomerLastName,
            CustomerFullName: fullName,
            CustomerPhone: order.CustomerPhone,
            CustomerEmail: order.CustomerEmail,
            CustomerComment: order.CustomerComment,
            DeliveryCityRef: order.DeliveryCityRef,
            DeliveryCityName: order.DeliveryCityName,
            DeliveryWarehouseRef: order.DeliveryWarehouseRef,
            DeliveryWarehouseName: order.DeliveryWarehouseName,
            OrderStatus: order.OrderStatus,
            PaymentMethod: order.PaymentMethod,
            PaymentStatus: order.PaymentStatus,
            TotalPrice: order.TotalPrice,
            TotalProfit: order.TotalProfit,
            AdminComment: order.AdminComment,
            CreatedAt: order.CreatedAt,
            UpdatedAt: order.UpdatedAt,
            Items: order.Items
                .OrderBy(item => item.ProductId)
                .Select(item => new AdminOrderItemResponse(
                    item.ProductId,
                    item.ProductNameSnapshot,
                    item.Quantity,
                    item.UnitPrice,
                    item.UnitDropPrice,
                    item.UnitPrice * item.Quantity,
                    (item.UnitPrice - item.UnitDropPrice) * item.Quantity))
                .ToArray(),
            StatusHistory: order.StatusHistory
                .OrderByDescending(h => h.CreatedAt)
                .Select(h => new OrderStatusHistoryDto(
                    h.Id,
                    h.OldStatus,
                    h.NewStatus,
                    h.ChangedByAdminId,
                    h.ChangedByAdmin != null ? (h.ChangedByAdmin.Username ?? h.ChangedByAdmin.Name) : null,
                    h.Comment,
                    h.CreatedAt))
                .ToArray());
    }
}
