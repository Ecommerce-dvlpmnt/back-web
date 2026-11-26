using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Exceptions;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Application.Services;

public sealed class OrderService(
    AppDbContext dbContext,
    IReferralService referralService,
    ITelegramBotClient telegramBotClient,
    IAuditLogService auditLogService,
    ILogger<OrderService>? logger = null) : IOrderService
{
    public async Task<OrderResponse> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(request));

        var (firstName, lastName) = NormalizeCustomerName(
            request.CustomerFirstName,
            request.CustomerLastName);

        if (!PhoneNumberNormalizer.TryNormalize(request.CustomerPhone, out var phone))
        {
            throw new ArgumentException(
                "Некоректний номер телефону. Вкажіть український номер у форматі +380XXXXXXXXX.",
                nameof(request));
        }

        var email = request.CustomerEmail?.Trim();
        var cityName = RequireDeliveryField(
            request.DeliveryCityName,
            "Вкажіть місто доставки.",
            150,
            "Назва міста не може перевищувати 150 символів.");
        var warehouseName = RequireDeliveryField(
            request.DeliveryWarehouseName,
            "Вкажіть відділення або поштомат Нової Пошти.",
            250,
            "Назва відділення не може перевищувати 250 символів.");

        if (!string.IsNullOrWhiteSpace(request.DeliveryType)
            && !warehouseName.Contains(request.DeliveryType, StringComparison.OrdinalIgnoreCase))
        {
            warehouseName = $"{request.DeliveryType}: {warehouseName}";
        }

        var comment = request.CustomerComment?.Trim();

        var requestedQuantities = AggregateQuantities(request.Items);
        var productIds = requestedQuantities.Keys.Order().ToArray();

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var products = await dbContext.Products
            .FromSqlInterpolated(
                $"SELECT * FROM \"Products\" WHERE \"Id\" = ANY ({productIds}) ORDER BY \"Id\" FOR UPDATE")
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var inactiveProducts = products.Values.Where(product => !product.IsActive).ToList();
        if (inactiveProducts.Count > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            var names = string.Join(", ", inactiveProducts.Select(product => $"'{product.Name}'"));
            throw new InvalidOperationException($"Товар {names} знято з продажу та недоступний для замовлення.");
        }

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

        await referralService.AttachReferrerAsync(order, request.ReferralCode, cancellationToken);

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        Partner? attachedPartner = null;
        if (order.ReferrerId.HasValue)
        {
            attachedPartner = await dbContext.Partners
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == order.ReferrerId.Value, cancellationToken);

            if (attachedPartner is not null && order.ReferralRewardAmount > 0m)
            {
                _ = telegramBotClient.SendReferralRewardPendingNotificationAsync(
                    new ReferralRewardResult(
                        order.Id,
                        order.OrderNumber,
                        attachedPartner.Id,
                        attachedPartner.TelegramUserId,
                        attachedPartner.Name,
                        order.TotalPrice,
                        attachedPartner.CommissionPercent,
                        order.ReferralRewardAmount,
                        attachedPartner.TotalEarned,
                        ReferralRewardStatus.Pending),
                    CancellationToken.None);
            }
        }

        PromoCode? attachedPromo = null;
        if (order.PromoCodeId.HasValue)
        {
            attachedPromo = await dbContext.PromoCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == order.PromoCodeId.Value, cancellationToken);
        }

        // Для накладеного платежу надсилаємо сповіщення адмінам одразу.
        // Для онлайн-оплати (Monobank) надсилаємо повну картку замовлення в момент фактичної оплати (ConfirmPaymentAsync),
        // щоб у бот не летіли неоплачені чернетки зі статусом "Нове".
        if (order.PaymentMethod != PaymentMethod.Online)
        {
            _ = telegramBotClient.SendAdminNewOrderNotificationAsync(
                BuildAdminOrderNotificationDto(order, attachedPromo, attachedPartner),
                CancellationToken.None);
        }

        logger?.LogInformation(
            "Замовлення #{OrderNumber} успішно створено (PublicId: {PublicId}). Позицій: {ItemCount}, загальна сума: {TotalPrice} грн.",
            order.OrderNumber,
            order.PublicId,
            order.Items.Count,
            order.TotalPrice);

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
            .Include(o => o.Referrer)
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

    public async Task<OrderResponse> CancelByPublicIdAsync(
        string publicId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await dbContext.Orders
            .FromSqlInterpolated(
                $"SELECT * FROM \"Orders\" WHERE \"PublicId\" = {publicId} FOR UPDATE")
            .Include(o => o.Items)
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            throw new KeyNotFoundException(
                $"Замовлення з публічним кодом '{publicId}' не знайдено.");
        }

        var decision = OrderCancellationPolicy.Check(order.OrderStatus, order.PaymentStatus);
        if (decision == OrderCancellationDecision.AlreadyCancelled)
        {
            return MapToResponse(order);
        }

        if (decision == OrderCancellationDecision.NotNewStatus)
        {
            throw new InvalidOperationException(
                "Скасувати можна лише нове замовлення, яке ще не підтверджено.");
        }

        if (decision == OrderCancellationDecision.Paid)
        {
            throw new InvalidOperationException(
                "Оплачене замовлення не можна скасувати самостійно. Зверніться до підтримки.");
        }

        var oldStatus = order.OrderStatus;
        var now = DateTime.UtcNow;

        await ReturnStockAsync(order, now, cancellationToken);

        order.OrderStatus = OrderStatus.Cancelled;
        order.UpdatedAt = now;

        dbContext.OrderStatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            OldStatus = oldStatus,
            NewStatus = OrderStatus.Cancelled,
            ChangedByAdminId = null,
            Comment = "Скасовано клієнтом",
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        var cancelledReward = await referralService.ProcessCancelledOrderRewardAsync(order.Id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (cancelledReward is not null)
        {
            _ = telegramBotClient.SendReferralRewardCancelledNotificationAsync(cancelledReward, "скасовано клієнтом", CancellationToken.None);
        }

        logger?.LogInformation(
            "Замовлення #{OrderNumber} (PublicId: {PublicId}) скасовано клієнтом. Товари повернено на склад.",
            order.OrderNumber,
            order.PublicId);

        return MapToResponse(order);
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
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await dbContext.Orders
            .FromSqlInterpolated(
                $"SELECT * FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            throw new KeyNotFoundException($"Замовлення з ID '{id}' не знайдено.");
        }

        await dbContext.Entry(order)
            .Collection(o => o.Items)
            .LoadAsync(cancellationToken);

        var oldStatus = order.OrderStatus;
        var newStatus = request.Status;

        if (oldStatus == newStatus)
        {
            await transaction.CommitAsync(cancellationToken);
            return MapToResponse(order);
        }

        var now = DateTime.UtcNow;

        if (OrderStockTransitionPolicy.RequiresStockReturn(oldStatus, newStatus))
        {
            await ReturnStockAsync(order, now, cancellationToken);
        }
        else if (OrderStockTransitionPolicy.RequiresStockDeduction(oldStatus, newStatus))
        {
            await DeductStockAsync(order, now, cancellationToken);
            if (order.ReferrerId.HasValue && order.ReferralRewardStatus == ReferralRewardStatus.Cancelled)
            {
                order.ReferralRewardStatus = ReferralRewardStatus.Pending;
            }
        }

        order.OrderStatus = newStatus;
        order.UpdatedAt = now;

        dbContext.OrderStatusHistory.Add(new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            OldStatus = oldStatus,
            NewStatus = newStatus,
            ChangedByAdminId = adminId,
            Comment = request.Comment,
            CreatedAt = now
        });

        await auditLogService.LogAsync(
            AuditLogActions.OrderStatusChanged,
            AuditLogEntityTypes.Order,
            order.Id.ToString(),
            new
            {
                orderNumber = order.OrderNumber,
                publicId = order.PublicId,
                before = new { status = oldStatus.ToString() },
                after = new { status = newStatus.ToString() },
                changedFields = new[] { "status" },
                comment = request.Comment
            },
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        ReferralRewardResult? deliveredReward = null;
        ReferralRewardResult? cancelledReward = null;

        if (newStatus == OrderStatus.Delivered)
        {
            deliveredReward = await referralService.ProcessDeliveredOrderRewardAsync(order.Id, cancellationToken);
        }
        else if (newStatus is OrderStatus.Cancelled or OrderStatus.Refused or OrderStatus.Returned)
        {
            cancelledReward = await referralService.ProcessCancelledOrderRewardAsync(order.Id, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (deliveredReward is not null)
        {
            _ = telegramBotClient.SendReferralRewardDeliveredNotificationAsync(deliveredReward, CancellationToken.None);
        }
        else if (cancelledReward is not null)
        {
            var reason = newStatus switch
            {
                OrderStatus.Refused => "відмова на пошті",
                OrderStatus.Returned => "повернення товару",
                _ => "скасовано"
            };
            _ = telegramBotClient.SendReferralRewardCancelledNotificationAsync(cancelledReward, reason, CancellationToken.None);
        }

        return MapToResponse(order);
    }

    public async Task<AdminOrderDetailResponse?> UpdateCommentAsync(
        Guid id,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .Include(o => o.Items)
            .Include(o => o.Referrer)
            .Include(o => o.StatusHistory)
                .ThenInclude(h => h.ChangedByAdmin)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null) return null;

        order.AdminComment = comment;
        order.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapToAdminDetailResponse(order);
    }

    public async Task<OrderPaymentConfirmationResponse> ConfirmPaymentAsync(
        Guid id,
        ConfirmOrderPaymentRequest? request = null,
        Guid? adminId = null,
        CancellationToken cancellationToken = default)
    {
        ReferralRewardResult? referralReward;
        Order order;
        var newlyPaid = false;

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            order = await dbContext.Orders
                .FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE")
                .Include(o => o.Items)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Замовлення з ID '{id}' не знайдено.");

            if (order.OrderStatus is OrderStatus.Cancelled or OrderStatus.Refused or OrderStatus.Returned)
            {
                throw new InvalidOperationException("Не можна підтвердити оплату для скасованого або поверненого замовлення.");
            }

            var now = DateTime.UtcNow;
            if (order.PaymentStatus != PaymentStatus.Paid)
            {
                order.PaymentStatus = PaymentStatus.Paid;
                order.UpdatedAt = now;
                newlyPaid = true;
            }

            ApplyPaymentMetadata(order, request?.PaymentProvider, request?.ExternalPaymentId, request?.Comment, now);

            await dbContext.SaveChangesAsync(cancellationToken);

            referralReward = await referralService.ProcessPaidOrderAsync(order.Id, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        logger?.LogInformation(
            "Оплату замовлення #{OrderNumber} (PublicId: {PublicId}) підтверджено (newlyPaid={NewlyPaid}).",
            order.OrderNumber,
            order.PublicId,
            newlyPaid);

        if (referralReward is not null)
        {
            _ = telegramBotClient.SendReferralRewardNotificationAsync(referralReward, CancellationToken.None);
        }

        var adminNotificationSent = false;
        if (newlyPaid)
        {
            PromoCode? attachedPromo = null;
            if (order.PromoCodeId.HasValue)
            {
                attachedPromo = await dbContext.PromoCodes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == order.PromoCodeId.Value, CancellationToken.None);
            }

            Partner? attachedPartner = null;
            if (order.ReferrerId.HasValue)
            {
                attachedPartner = await dbContext.Partners
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == order.ReferrerId.Value, CancellationToken.None);
            }

            adminNotificationSent = await telegramBotClient.SendAdminNewOrderNotificationAsync(
                BuildAdminOrderNotificationDto(order, attachedPromo, attachedPartner),
                CancellationToken.None);
        }

        return new OrderPaymentConfirmationResponse(
            MapToResponse(order),
            referralReward,
            adminNotificationSent);
    }

    private static AdminBotOrderNotificationDto BuildAdminOrderNotificationDto(
        Order order,
        PromoCode? promo,
        Partner? referrer) =>
        new(
            order.Id,
            order.OrderNumber,
            order.PublicId,
            $"{order.CustomerFirstName} {order.CustomerLastName}".Trim(),
            order.CustomerPhone,
            order.DeliveryCityName,
            order.DeliveryWarehouseName,
            order.PaymentMethod,
            order.OrderStatus,
            order.TotalPrice,
            order.DiscountAmount,
            promo?.Code,
            promo?.DiscountPercent,
            referrer?.Name,
            referrer?.ReferralCode,
            order.ReferralRewardAmount,
            order.CreatedAt,
            order.Items
                .Select(i => new AdminBotOrderItemDto(
                    i.ProductNameSnapshot,
                    i.Quantity,
                    i.UnitPrice,
                    i.UnitPrice * i.Quantity))
                .ToArray(),
            order.PaymentStatus);

    public async Task<OrderPaymentConfirmationResponse> ProcessPaymentWebhookAsync(
        PaymentWebhookRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var targetOrderId = await ResolveWebhookOrderIdAsync(request, cancellationToken);

        if (request.PaymentStatus == PaymentStatus.Paid)
        {
            var confirmRequest = new ConfirmOrderPaymentRequest
            {
                PaymentProvider = request.PaymentProvider,
                ExternalPaymentId = request.ExternalPaymentId,
                Comment = request.Comment
            };

            return await ConfirmPaymentAsync(targetOrderId, confirmRequest, null, cancellationToken);
        }

        var isRelational = dbContext.Database.IsRelational();
        await using var transaction = isRelational
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var order = isRelational
            ? await dbContext.Orders
                .FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {targetOrderId} FOR UPDATE")
                .Include(o => o.Items)
                .FirstOrDefaultAsync(cancellationToken)
            : await dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == targetOrderId, cancellationToken);

        if (order is null)
        {
            throw new KeyNotFoundException($"Замовлення з ID '{targetOrderId}' не знайдено.");
        }

        if (order.PaymentStatus == PaymentStatus.Paid && request.PaymentStatus == PaymentStatus.Failed)
        {
            if (request.CancelOrderOnFailure)
            {
                return new OrderPaymentConfirmationResponse(MapToResponse(order), null, false);
            }

            throw new InvalidOperationException("Не можна змінити статус вже оплаченого замовлення на Failed.");
        }

        // Якщо клієнт вже створив новий інвойс, ігноруємо запізнілий expired від старого інвойсу
        if (request.CancelOrderOnFailure
            && !string.IsNullOrWhiteSpace(order.ExternalPaymentId)
            && !string.IsNullOrWhiteSpace(request.ExternalPaymentId)
            && !string.Equals(order.ExternalPaymentId, request.ExternalPaymentId.Trim(), StringComparison.Ordinal))
        {
            return new OrderPaymentConfirmationResponse(MapToResponse(order), null, false);
        }

        var now = DateTime.UtcNow;
        order.PaymentStatus = request.PaymentStatus;
        order.UpdatedAt = now;

        ApplyPaymentMetadata(order, request.PaymentProvider, request.ExternalPaymentId, request.Comment, now);

        var autoCancelledOnExpire = false;
        if (request.CancelOrderOnFailure
            && request.PaymentStatus == PaymentStatus.Failed
            && order.OrderStatus == OrderStatus.New)
        {
            var oldStatus = order.OrderStatus;
            await ReturnStockAsync(order, now, cancellationToken);

            if (order.PromoCodeId.HasValue)
            {
                var promo = isRelational
                    ? await dbContext.PromoCodes
                        .FromSqlInterpolated($"SELECT * FROM \"PromoCodes\" WHERE \"Id\" = {order.PromoCodeId.Value} FOR UPDATE")
                        .FirstOrDefaultAsync(cancellationToken)
                    : await dbContext.PromoCodes
                        .FirstOrDefaultAsync(p => p.Id == order.PromoCodeId.Value, cancellationToken);

                if (promo is not null && promo.UsedCount > 0)
                {
                    promo.UsedCount -= 1;
                    if (!promo.MaxUses.HasValue || promo.MaxUses.Value <= 0 || promo.UsedCount < promo.MaxUses.Value)
                    {
                        promo.IsActive = true;
                    }
                }
            }

            order.OrderStatus = OrderStatus.Cancelled;
            order.UpdatedAt = now;
            autoCancelledOnExpire = true;

            dbContext.OrderStatusHistory.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                OldStatus = oldStatus,
                NewStatus = OrderStatus.Cancelled,
                ChangedByAdminId = null,
                Comment = "Автоматично скасовано: минув час оплати рахунку Monobank",
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        ReferralRewardResult? cancelledReward = null;
        if (request.PaymentStatus == PaymentStatus.Refunded || autoCancelledOnExpire)
        {
            cancelledReward = await referralService.ProcessCancelledOrderRewardAsync(order.Id, cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        if (autoCancelledOnExpire)
        {
            logger?.LogInformation(
                "Замовлення #{OrderNumber} (PublicId: {PublicId}) автоматично скасовано через закінчення дії рахунку Monobank. Товари повернено на склад.",
                order.OrderNumber,
                order.PublicId);
        }

        var telegramNotificationSent = false;
        if (cancelledReward is not null)
        {
            var cancelReason = autoCancelledOnExpire ? "минув час онлайн-оплати" : "повернення коштів";
            telegramNotificationSent = await telegramBotClient.SendReferralRewardCancelledNotificationAsync(
                cancelledReward,
                cancelReason,
                cancellationToken);
        }

        return new OrderPaymentConfirmationResponse(
            MapToResponse(order),
            cancelledReward,
            telegramNotificationSent);
    }

    private async Task<Guid> ResolveWebhookOrderIdAsync(
        PaymentWebhookRequest request,
        CancellationToken cancellationToken)
    {
        if (request.OrderId.HasValue)
        {
            return request.OrderId.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.OrderPublicId))
        {
            var publicId = request.OrderPublicId.Trim();
            var byPublicId = await dbContext.Orders
                .AsNoTracking()
                .Where(o => o.PublicId == publicId)
                .Select(o => (Guid?)o.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return byPublicId
                ?? throw new KeyNotFoundException($"Замовлення з публічним кодом '{publicId}' не знайдено.");
        }

        if (!string.IsNullOrWhiteSpace(request.ExternalPaymentId))
        {
            var externalId = request.ExternalPaymentId.Trim();
            var byExternalId = await dbContext.Orders
                .AsNoTracking()
                .Where(o => o.ExternalPaymentId == externalId)
                .Select(o => (Guid?)o.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return byExternalId
                ?? throw new KeyNotFoundException($"Замовлення із зовнішнім ID платежу '{externalId}' не знайдено.");
        }

        throw new ArgumentException("Потрібно вказати OrderId, OrderPublicId або ExternalPaymentId.", nameof(request));
    }

    private static void ApplyPaymentMetadata(
        Order order,
        string? paymentProvider,
        string? externalPaymentId,
        string? comment,
        DateTime now)
    {
        if (!string.IsNullOrWhiteSpace(paymentProvider))
        {
            order.PaymentProvider = paymentProvider.Trim();
            order.UpdatedAt = now;
        }

        if (!string.IsNullOrWhiteSpace(externalPaymentId))
        {
            order.ExternalPaymentId = externalPaymentId.Trim();
            order.UpdatedAt = now;
        }

        if (!string.IsNullOrWhiteSpace(comment))
        {
            var trimmedComment = comment.Trim();
            if (string.IsNullOrWhiteSpace(order.AdminComment))
            {
                order.AdminComment = trimmedComment;
                order.UpdatedAt = now;
            }
            else if (!order.AdminComment.Contains(trimmedComment, StringComparison.Ordinal))
            {
                order.AdminComment = $"{order.AdminComment}\n{trimmedComment}";
                order.UpdatedAt = now;
            }
        }
    }

    private async Task ReturnStockAsync(
        Order order,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var productIds = order.Items.Select(i => i.ProductId).Distinct().Order().ToArray();
        var products = dbContext.Database.IsRelational()
            ? await dbContext.Products
                .FromSqlInterpolated(
                    $"SELECT * FROM \"Products\" WHERE \"Id\" = ANY ({productIds}) ORDER BY \"Id\" FOR UPDATE")
                .ToDictionaryAsync(p => p.Id, cancellationToken)
            : await dbContext.Products
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

        foreach (var item in order.Items)
        {
            if (products.TryGetValue(item.ProductId, out var product))
            {
                product.Stock += item.Quantity;
                product.StockUpdatedAt = now;
            }
        }
    }

    private async Task DeductStockAsync(
        Order order,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var requestedQuantities = order.Items
            .GroupBy(item => item.ProductId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.Quantity));

        var productIds = requestedQuantities.Keys.Order().ToArray();
        var products = await dbContext.Products
            .FromSqlInterpolated(
                $"SELECT * FROM \"Products\" WHERE \"Id\" = ANY ({productIds}) ORDER BY \"Id\" FOR UPDATE")
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var inactiveProducts = products.Values.Where(product => !product.IsActive).ToList();
        if (inactiveProducts.Count > 0)
        {
            var names = string.Join(", ", inactiveProducts.Select(product => $"'{product.Name}'"));
            throw new InvalidOperationException($"Товар {names} знято з продажу та недоступний для замовлення.");
        }

        var shortages = OrderStockValidator.GetShortages(requestedQuantities, products);
        if (shortages.Count > 0)
        {
            throw new InsufficientStockException(shortages);
        }

        foreach (var item in order.Items)
        {
            var product = products[item.ProductId];
            product.Stock -= item.Quantity;
            product.StockUpdatedAt = now;
        }
    }

    private static (string FirstName, string LastName) NormalizeCustomerName(
        string? firstNameValue,
        string? lastNameValue)
    {
        var firstName = firstNameValue?.Trim();
        if (string.IsNullOrWhiteSpace(firstName) || firstName.Length < 2)
            throw new ArgumentException("Вкажіть ім'я покупця (мінімум 2 символи).", nameof(firstNameValue));

        if (firstName.Length > 100)
            throw new ArgumentException("Ім'я покупця не може перевищувати 100 символів.", nameof(firstNameValue));

        var lastName = lastNameValue?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(lastName))
        {
            var parts = firstName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                firstName = parts[0];
                lastName = parts[1].Trim();
            }
            else
            {
                lastName = firstName;
            }
        }

        if (firstName.Length < 2)
            throw new ArgumentException("Вкажіть ім'я покупця (мінімум 2 символи).", nameof(firstNameValue));

        if (lastName.Length > 100)
            throw new ArgumentException("Прізвище покупця не може перевищувати 100 символів.", nameof(lastNameValue));

        return (firstName, lastName);
    }

    private static string RequireDeliveryField(
        string? value,
        string requiredMessage,
        int maxLength,
        string maxLengthMessage)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException(requiredMessage, nameof(value));

        if (normalized.Length > maxLength)
            throw new ArgumentException(maxLengthMessage, nameof(value));

        return normalized;
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
                .ToArray(),
            ReferrerId: order.ReferrerId);
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
                .ToArray(),
            ReferrerId: order.ReferrerId,
            ReferrerName: order.Referrer?.Name,
            ReferralCode: order.Referrer?.ReferralCode,
            ReferralRewardAmount: order.ReferralRewardAmount,
            ReferralRewardStatus: order.ReferralRewardStatus);
    }
}
