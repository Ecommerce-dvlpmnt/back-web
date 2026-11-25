using System.Security.Cryptography;
using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Application.Services;

public sealed class ReferralService(
    AppDbContext dbContext,
    ITelegramBotClient? telegramBotClient = null,
    ILogger<ReferralService>? logger = null) : IReferralService
{
    public async Task<Partner?> FindPartnerByCodeAsync(
        string? referralCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = referralCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return await dbContext.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ReferralCode.ToUpper() == normalized, cancellationToken);
    }

    public async Task<bool> AttachReferrerAsync(
        Order order,
        string? referralCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var normalized = referralCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        var partner = await FindPartnerByCodeAsync(normalized, cancellationToken);
        if (partner is not null)
        {
            var reward = Math.Round(
                order.TotalPrice * partner.CommissionPercent / 100m,
                2,
                MidpointRounding.AwayFromZero);

            order.ReferrerId = partner.Id;
            order.ReferralRewardAmount = reward;
            order.ReferralRewardStatus = ReferralRewardStatus.Pending;
            return true;
        }

        var promo = dbContext.Database.IsRelational()
            ? await dbContext.PromoCodes
                .FromSqlInterpolated($"SELECT * FROM \"PromoCodes\" WHERE UPPER(\"Code\") = {normalized} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken)
            : await dbContext.PromoCodes
                .FirstOrDefaultAsync(p => p.Code.ToUpper() == normalized, cancellationToken);

        if (promo is null || !promo.IsActive || (promo.MaxUses.HasValue && promo.MaxUses.Value > 0 && promo.UsedCount >= promo.MaxUses.Value))
        {
            return false;
        }

        var discount = Math.Round(
            order.TotalPrice * promo.DiscountPercent / 100m,
            2,
            MidpointRounding.AwayFromZero);

        order.PromoCodeId = promo.Id;
        order.DiscountAmount = discount;
        order.TotalPrice = Math.Max(0m, order.TotalPrice - discount);
        order.TotalProfit -= discount;

        promo.UsedCount += 1;
        if (promo.MaxUses.HasValue && promo.MaxUses.Value > 0 && promo.UsedCount >= promo.MaxUses.Value)
        {
            promo.IsActive = false;
        }

        return true;
    }

    public async Task<ReferralRewardResult?> ProcessPaidOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null || order.ReferrerId is null)
        {
            return null;
        }

        var partner = await dbContext.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == order.ReferrerId.Value, cancellationToken);

        if (partner is null)
        {
            return null;
        }

        return new ReferralRewardResult(
            order.Id,
            order.OrderNumber,
            partner.Id,
            partner.TelegramUserId,
            partner.Name,
            order.TotalPrice,
            partner.CommissionPercent,
            order.ReferralRewardAmount,
            partner.TotalEarned,
            order.ReferralRewardStatus);
    }

    public async Task<ReferralRewardResult?> ProcessDeliveredOrderRewardAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var order = await dbContext.Orders
                .FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (order is null)
            {
                throw new KeyNotFoundException($"Замовлення з ID '{orderId}' не знайдено.");
            }

            if (order.ReferrerId is null || order.ReferralRewardStatus != ReferralRewardStatus.Pending)
            {
                return null;
            }

            var partnerId = order.ReferrerId.Value;
            var partner = await dbContext.Partners
                .FromSqlInterpolated($"SELECT * FROM \"Referrals\" WHERE \"Id\" = {partnerId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (partner is null)
            {
                return null;
            }

            var reward = order.ReferralRewardAmount > 0m
                ? order.ReferralRewardAmount
                : Math.Round(order.TotalPrice * partner.CommissionPercent / 100m, 2, MidpointRounding.AwayFromZero);

            partner.TotalEarned += reward;
            order.ReferralRewardAmount = reward;
            order.ReferralRewardStatus = ReferralRewardStatus.Paid;
            order.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            logger?.LogInformation(
                "Реферальну винагороду {Reward} грн нараховано партнеру {PartnerId} за доставлене замовлення #{OrderNumber}.",
                reward,
                partner.Id,
                order.OrderNumber);

            return new ReferralRewardResult(
                order.Id,
                order.OrderNumber,
                partner.Id,
                partner.TelegramUserId,
                partner.Name,
                order.TotalPrice,
                partner.CommissionPercent,
                reward,
                partner.TotalEarned,
                ReferralRewardStatus.Paid);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<ReferralRewardResult?> ProcessCancelledOrderRewardAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var order = await dbContext.Orders
                .FromSqlInterpolated($"SELECT * FROM \"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (order is null)
            {
                throw new KeyNotFoundException($"Замовлення з ID '{orderId}' не знайдено.");
            }

            if (order.ReferrerId is null || order.ReferralRewardStatus is ReferralRewardStatus.None or ReferralRewardStatus.Cancelled)
            {
                return null;
            }

            var partnerId = order.ReferrerId.Value;
            var partner = await dbContext.Partners
                .FromSqlInterpolated($"SELECT * FROM \"Referrals\" WHERE \"Id\" = {partnerId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (partner is null)
            {
                return null;
            }

            if (order.ReferralRewardStatus == ReferralRewardStatus.Paid)
            {
                partner.TotalEarned = Math.Max(0m, partner.TotalEarned - order.ReferralRewardAmount);
            }

            var previousReward = order.ReferralRewardAmount;
            order.ReferralRewardStatus = ReferralRewardStatus.Cancelled;
            order.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            logger?.LogInformation(
                "Реферальну винагороду {Reward} грн скасовано для партнера {PartnerId} за замовлення #{OrderNumber}.",
                previousReward,
                partner.Id,
                order.OrderNumber);

            return new ReferralRewardResult(
                order.Id,
                order.OrderNumber,
                partner.Id,
                partner.TelegramUserId,
                partner.Name,
                order.TotalPrice,
                partner.CommissionPercent,
                previousReward,
                partner.TotalEarned,
                ReferralRewardStatus.Cancelled);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<PartnerResponse>> GetAllPartnersAsync(
        CancellationToken cancellationToken = default)
    {
        var partners = await dbContext.Partners
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        var partnerIds = partners.Select(p => p.Id).ToArray();

        var orderStats = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.ReferrerId.HasValue && partnerIds.Contains(o.ReferrerId.Value))
            .GroupBy(o => o.ReferrerId!.Value)
            .Select(g => new
            {
                PartnerId = g.Key,
                PendingSum = g.Where(x => x.ReferralRewardStatus == ReferralRewardStatus.Pending).Sum(x => x.ReferralRewardAmount),
                TotalOrders = g.Count(),
                DeliveredOrders = g.Count(x => x.OrderStatus == OrderStatus.Delivered),
                TotalTurnover = g.Where(x => x.OrderStatus != OrderStatus.Cancelled && x.OrderStatus != OrderStatus.Refused).Sum(x => x.TotalPrice)
            })
            .ToDictionaryAsync(x => x.PartnerId, cancellationToken);

        var partnersById = partners.ToDictionary(p => p.Id);
        var invitedLookup = partners
            .Where(p => p.InvitedByPartnerId.HasValue)
            .GroupBy(p => p.InvitedByPartnerId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        return partners.Select(p =>
        {
            var stats = orderStats.GetValueOrDefault(p.Id);
            var inviter = p.InvitedByPartnerId.HasValue
                ? partnersById.GetValueOrDefault(p.InvitedByPartnerId.Value)
                : null;
            var invitedList = invitedLookup.GetValueOrDefault(p.Id);
            var invitedTurnover = invitedList?.Sum(x => orderStats.GetValueOrDefault(x.Id)?.TotalTurnover ?? 0m) ?? 0m;
            var invitedEarned = invitedList?.Sum(x => x.TotalEarned + (orderStats.GetValueOrDefault(x.Id)?.PendingSum ?? 0m)) ?? 0m;
            var invitedNames = invitedList?.Select(x => x.Name).ToArray();

            return new PartnerResponse(
                p.Id,
                p.TelegramUserId,
                p.Name,
                p.ReferralCode,
                p.CommissionPercent,
                p.TotalEarned,
                p.CreatedAt,
                stats?.PendingSum ?? 0m,
                p.TotalEarned,
                stats?.TotalOrders ?? 0,
                stats?.DeliveredOrders ?? 0,
                stats?.TotalTurnover ?? 0m,
                p.CardNumber,
                p.CardHolderName,
                inviter?.Name,
                inviter?.ReferralCode,
                invitedList?.Count ?? 0,
                invitedNames,
                invitedTurnover,
                invitedEarned);
        }).ToArray();
    }

    public async Task<PartnerResponse?> GetPartnerByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var partner = await dbContext.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (partner is null)
        {
            return null;
        }

        var orders = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.ReferrerId == partner.Id)
            .Select(o => new { o.OrderStatus, o.ReferralRewardStatus, o.ReferralRewardAmount, o.TotalPrice })
            .ToListAsync(cancellationToken);

        var pendingSum = orders
            .Where(o => o.ReferralRewardStatus == ReferralRewardStatus.Pending)
            .Sum(o => o.ReferralRewardAmount);

        var totalOrders = orders.Count;
        var deliveredOrders = orders.Count(o => o.OrderStatus == OrderStatus.Delivered);
        var totalTurnover = orders
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && o.OrderStatus != OrderStatus.Refused)
            .Sum(o => o.TotalPrice);

        return new PartnerResponse(
            partner.Id,
            partner.TelegramUserId,
            partner.Name,
            partner.ReferralCode,
            partner.CommissionPercent,
            partner.TotalEarned,
            partner.CreatedAt,
            pendingSum,
            partner.TotalEarned,
            totalOrders,
            deliveredOrders,
            totalTurnover,
            partner.CardNumber,
            partner.CardHolderName);
    }

    public async Task<PartnerResponse> CreatePartnerAsync(
        CreatePartnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TelegramUserId <= 0)
        {
            throw new ArgumentException("TelegramUserId має бути додатним числом.", nameof(request));
        }

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Ім'я партнера є обов'язковим.", nameof(request));
        }

        var code = request.ReferralCode.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Реферальний код є обов'язковим.", nameof(request));
        }

        var duplicateCode = await dbContext.Partners
            .AnyAsync(p => p.ReferralCode.ToUpper() == code, cancellationToken);
        if (duplicateCode)
        {
            throw new InvalidOperationException($"Партнер з реферальним кодом '{code}' вже існує.");
        }

        var duplicateTelegram = await dbContext.Partners
            .AnyAsync(p => p.TelegramUserId == request.TelegramUserId, cancellationToken);
        if (duplicateTelegram)
        {
            throw new InvalidOperationException($"Партнер з TelegramUserId '{request.TelegramUserId}' вже існує.");
        }

        var cleanCard = string.IsNullOrWhiteSpace(request.CardNumber)
            ? null
            : new string(request.CardNumber.Where(char.IsDigit).ToArray());

        var partner = new Partner
        {
            Id = Guid.NewGuid(),
            TelegramUserId = request.TelegramUserId,
            Name = name,
            ReferralCode = code,
            CardNumber = string.IsNullOrEmpty(cleanCard) ? null : cleanCard,
            CardHolderName = request.CardHolderName?.Trim(),
            CommissionPercent = request.CommissionPercent,
            TotalEarned = 0m,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Partners.Add(partner);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToPartnerResponse(partner, 0m);
    }

    public async Task<PartnerResponse> UpdatePartnerAsync(
        Guid id,
        UpdateReferralPartnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var partner = await dbContext.Partners
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (partner is null)
        {
            throw new KeyNotFoundException($"Реферала з ID '{id}' не знайдено.");
        }

        if (request.CommissionPercent.HasValue)
        {
            if (request.CommissionPercent.Value <= 0 || request.CommissionPercent.Value > 100)
            {
                throw new ArgumentException("Комісійний відсоток має бути від 0.01% до 100%.", nameof(request));
            }
            partner.CommissionPercent = request.CommissionPercent.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            partner.Name = request.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.ReferralCode))
        {
            var normalizedCode = request.ReferralCode.Trim().ToUpperInvariant();
            var duplicate = await dbContext.Partners
                .AnyAsync(p => p.Id != id && p.ReferralCode.ToUpper() == normalizedCode, cancellationToken);

            if (duplicate)
            {
                throw new InvalidOperationException($"Реферальний код '{normalizedCode}' вже використовується іншим партнером.");
            }

            partner.ReferralCode = normalizedCode;
        }

        if (request.CardNumber is not null)
        {
            var cleanCard = new string(request.CardNumber.Where(char.IsDigit).ToArray());
            partner.CardNumber = string.IsNullOrEmpty(cleanCard) ? null : cleanCard;
        }

        if (request.CardHolderName is not null)
        {
            partner.CardHolderName = string.IsNullOrWhiteSpace(request.CardHolderName)
                ? null
                : request.CardHolderName.Trim();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetPartnerByIdAsync(id, cancellationToken)
            ?? MapToPartnerResponse(partner, 0m);
    }

    public async Task<ReferralCodeValidationResponse> ValidateCodeAsync(
        string? referralCode,
        CancellationToken cancellationToken = default)
    {
        var normalized = referralCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(normalized))
        {
            return new ReferralCodeValidationResponse(false, null, null);
        }

        var partner = await FindPartnerByCodeAsync(normalized, cancellationToken);
        if (partner is not null)
        {
            return new ReferralCodeValidationResponse(true, partner.ReferralCode, partner.Name, 0m, false);
        }

        var promo = await dbContext.PromoCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code.ToUpper() == normalized, cancellationToken);

        if (promo is not null && promo.IsActive && (!promo.MaxUses.HasValue || promo.MaxUses.Value <= 0 || promo.UsedCount < promo.MaxUses.Value))
        {
            return new ReferralCodeValidationResponse(
                true,
                promo.Code,
                $"Промокод -{promo.DiscountPercent:0.##}%",
                promo.DiscountPercent,
                true);
        }

        return new ReferralCodeValidationResponse(false, null, null);
    }

    public async Task<BotPartnerProfileResponse?> GetBotPartnerProfileAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        if (telegramUserId <= 0)
        {
            return null;
        }

        var partner = await dbContext.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TelegramUserId == telegramUserId, cancellationToken);

        if (partner is null)
        {
            return null;
        }

        var orders = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.ReferrerId == partner.Id)
            .Select(o => new { o.ReferralRewardAmount, o.ReferralRewardStatus, o.OrderStatus })
            .ToListAsync(cancellationToken);

        var pending = orders
            .Where(o => o.ReferralRewardStatus == ReferralRewardStatus.Pending)
            .Sum(o => o.ReferralRewardAmount);

        var deliveredCount = orders.Count(o => o.OrderStatus == OrderStatus.Delivered);

        return new BotPartnerProfileResponse(
            partner.Id,
            partner.TelegramUserId,
            partner.Name,
            partner.ReferralCode,
            partner.CommissionPercent,
            partner.TotalEarned,
            pending,
            orders.Count,
            deliveredCount,
            partner.CreatedAt,
            partner.CardNumber,
            partner.CardHolderName);
    }

    public async Task<BotPartnerProfileResponse> RegisterBotPartnerAsync(
        BotRegisterPartnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TelegramUserId <= 0)
        {
            throw new ArgumentException("TelegramUserId має бути додатним числом.", nameof(request));
        }

        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Ім'я партнера є обов'язковим.", nameof(request));
        }

        var existingPartner = await dbContext.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TelegramUserId == request.TelegramUserId, cancellationToken);

        if (existingPartner is not null)
        {
            throw new InvalidOperationException($"Партнер з Telegram ID '{request.TelegramUserId}' вже зареєстрований.");
        }

        var code = request.ReferralCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            code = await GenerateUniqueReferralCodeAsync(cancellationToken);
        }
        else
        {
            var codeExists = await dbContext.Partners
                .AnyAsync(p => p.ReferralCode.ToUpper() == code, cancellationToken);
            if (codeExists)
            {
                throw new InvalidOperationException($"Реферальний код '{code}' вже зайнятий.");
            }
        }

        Guid? invitedByPartnerId = null;
        if (!string.IsNullOrWhiteSpace(request.InvitedByReferralCode))
        {
            var inviter = await FindPartnerByCodeAsync(request.InvitedByReferralCode, cancellationToken);
            if (inviter is not null && inviter.TelegramUserId != request.TelegramUserId)
            {
                invitedByPartnerId = inviter.Id;
            }
        }

        var commission = request.CommissionPercent is > 0m and <= 100m
            ? Math.Round(request.CommissionPercent.Value, 2, MidpointRounding.AwayFromZero)
            : 3.0m;

        var partner = new Partner
        {
            Id = Guid.NewGuid(),
            TelegramUserId = request.TelegramUserId,
            Name = name,
            ReferralCode = code,
            CommissionPercent = commission,
            TotalEarned = 0m,
            InvitedByPartnerId = invitedByPartnerId,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Partners.Add(partner);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BotPartnerProfileResponse(
            partner.Id,
            partner.TelegramUserId,
            partner.Name,
            partner.ReferralCode,
            partner.CommissionPercent,
            partner.TotalEarned,
            0m,
            0,
            0,
            partner.CreatedAt,
            partner.CardNumber,
            partner.CardHolderName);
    }

    public async Task<BotPartnerStatsResponse?> GetBotPartnerStatsAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        if (telegramUserId <= 0)
        {
            return null;
        }

        var partner = await dbContext.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TelegramUserId == telegramUserId, cancellationToken);

        if (partner is null)
        {
            return null;
        }

        var orders = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.ReferrerId == partner.Id)
            .Select(o => new { o.TotalPrice, o.ReferralRewardAmount, o.ReferralRewardStatus, o.OrderStatus })
            .ToListAsync(cancellationToken);

        var pending = orders
            .Where(o => o.ReferralRewardStatus == ReferralRewardStatus.Pending)
            .Sum(o => o.ReferralRewardAmount);

        var totalTurnover = orders
            .Where(o => o.OrderStatus != OrderStatus.Cancelled && o.OrderStatus != OrderStatus.Refused && o.OrderStatus != OrderStatus.Returned)
            .Sum(o => o.TotalPrice);

        var deliveredCount = orders.Count(o => o.OrderStatus == OrderStatus.Delivered);
        var pendingCount = orders.Count(o => o.ReferralRewardStatus == ReferralRewardStatus.Pending);
        var cancelledCount = orders.Count(o => o.OrderStatus is OrderStatus.Cancelled or OrderStatus.Refused or OrderStatus.Returned);

        return new BotPartnerStatsResponse(
            partner.Name,
            partner.ReferralCode,
            partner.CommissionPercent,
            partner.TotalEarned,
            pending,
            totalTurnover,
            orders.Count,
            deliveredCount,
            pendingCount,
            cancelledCount,
            partner.CardNumber,
            partner.CardHolderName);
    }

    public async Task<BotPartnerOrdersResponse?> GetBotPartnerOrdersAsync(
        long telegramUserId,
        int limit = 20,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (telegramUserId <= 0)
        {
            return null;
        }

        var partner = await dbContext.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TelegramUserId == telegramUserId, cancellationToken);

        if (partner is null)
        {
            return null;
        }

        var clampedLimit = Math.Clamp(limit, 1, 100);
        var clampedOffset = Math.Max(0, offset);

        var query = dbContext.Orders
            .AsNoTracking()
            .Where(o => o.ReferrerId == partner.Id);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip(clampedOffset)
            .Take(clampedLimit)
            .Select(o => new BotPartnerOrderDto(
                o.OrderNumber,
                o.PublicId,
                o.TotalPrice,
                o.ReferralRewardAmount,
                o.OrderStatus,
                o.PaymentStatus,
                o.ReferralRewardStatus,
                o.CreatedAt))
            .ToListAsync(cancellationToken);

        return new BotPartnerOrdersResponse(totalCount, items);
    }

    public async Task<BotPayoutResponse> RequestPayoutAsync(
        long telegramUserId,
        BotPayoutRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Amount < 300m)
        {
            throw new ArgumentException("Мінімальна сума для виведення становить 300 грн.", nameof(request));
        }

        var cleanCard = string.IsNullOrWhiteSpace(request.CardNumber)
            ? string.Empty
            : new string(request.CardNumber.Where(char.IsDigit).ToArray());

        if (!string.IsNullOrWhiteSpace(request.CardNumber) && cleanCard.Length != 16)
        {
            throw new ArgumentException("Номер банківської картки повинен містити 16 цифр.", nameof(request));
        }

        if (!string.IsNullOrWhiteSpace(request.CardNumber) && !IsValidCardLuhn(cleanCard))
        {
            throw new ArgumentException("Некоректний номер банківської картки (перевірте правильність цифр).", nameof(request));
        }

        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var partner = await dbContext.Partners
                .FromSqlInterpolated($"SELECT * FROM \"Referrals\" WHERE \"TelegramUserId\" = {telegramUserId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (partner is null)
            {
                throw new KeyNotFoundException($"Партнера з Telegram ID '{telegramUserId}' не знайдено.");
            }

            if (cleanCard.Length == 0 && !string.IsNullOrWhiteSpace(partner.CardNumber))
            {
                cleanCard = new string(partner.CardNumber.Where(char.IsDigit).ToArray());
            }

            if (cleanCard.Length != 16)
            {
                throw new ArgumentException("Номер банківської картки повинен містити 16 цифр.", nameof(request));
            }

            if (partner.TotalEarned < request.Amount)
            {
                throw new InvalidOperationException(
                    $"Недостатньо коштів на балансі. Доступно для виведення: {partner.TotalEarned:0.00} грн.");
            }

            partner.TotalEarned -= request.Amount;
            partner.CardNumber = cleanCard;
            if (!string.IsNullOrWhiteSpace(request.CardHolderName))
            {
                partner.CardHolderName = request.CardHolderName.Trim();
            }

            var payout = new PartnerPayoutRequest
            {
                Id = Guid.NewGuid(),
                PartnerId = partner.Id,
                Amount = request.Amount,
                CardNumber = cleanCard,
                CardHolderName = request.CardHolderName?.Trim() ?? partner.CardHolderName,
                Status = PayoutRequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            dbContext.PartnerPayoutRequests.Add(payout);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            if (telegramBotClient is not null)
            {
                _ = telegramBotClient.SendAdminPayoutRequestNotificationAsync(
                    new AdminPayoutResponse(
                        payout.Id,
                        partner.Id,
                        partner.TelegramUserId,
                        partner.Name,
                        partner.ReferralCode,
                        payout.Amount,
                        payout.CardNumber,
                        payout.CardHolderName,
                        payout.Status,
                        payout.AdminComment,
                        payout.CreatedAt,
                        payout.ProcessedAt),
                    CancellationToken.None);
            }

            logger?.LogInformation(
                "Партнер {PartnerId} створив заявку на виведення {Amount} грн на картку {Card}.",
                partner.Id,
                payout.Amount,
                MaskCardNumber(cleanCard));

            return new BotPayoutResponse(
                payout.Id,
                payout.Amount,
                MaskCardNumber(cleanCard),
                payout.CardHolderName,
                payout.Status,
                partner.TotalEarned,
                payout.CreatedAt);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<IReadOnlyList<BotPayoutResponse>> GetBotPartnerPayoutsAsync(
        long telegramUserId,
        CancellationToken cancellationToken = default)
    {
        var partner = await dbContext.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TelegramUserId == telegramUserId, cancellationToken);

        if (partner is null)
        {
            return Array.Empty<BotPayoutResponse>();
        }

        var payouts = await dbContext.PartnerPayoutRequests
            .AsNoTracking()
            .Where(p => p.PartnerId == partner.Id)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return payouts
            .Select(p => new BotPayoutResponse(
                p.Id,
                p.Amount,
                MaskCardNumber(p.CardNumber),
                p.CardHolderName,
                p.Status,
                partner.TotalEarned,
                p.CreatedAt))
            .ToArray();
    }

    public async Task<IReadOnlyList<AdminPayoutResponse>> GetAllPayoutRequestsAsync(
        PayoutRequestStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.PartnerPayoutRequests
            .AsNoTracking()
            .Include(p => p.Partner)
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(p => p.Status == status.Value);
        }

        var list = await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return list
            .Select(p => new AdminPayoutResponse(
                p.Id,
                p.PartnerId,
                p.Partner?.TelegramUserId ?? 0L,
                p.Partner?.Name ?? string.Empty,
                p.Partner?.ReferralCode ?? string.Empty,
                p.Amount,
                p.CardNumber,
                p.CardHolderName,
                p.Status,
                p.AdminComment,
                p.CreatedAt,
                p.ProcessedAt))
            .ToArray();
    }

    public async Task<AdminPayoutResponse> ProcessPayoutRequestAsync(
        Guid payoutId,
        UpdatePayoutStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Status is not (PayoutRequestStatus.Completed or PayoutRequestStatus.Rejected))
        {
            throw new ArgumentException("Статус обробки заявки має бути Completed або Rejected.", nameof(request));
        }

        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var payout = await dbContext.PartnerPayoutRequests
                .FromSqlInterpolated($"SELECT * FROM \"ReferralPayoutRequests\" WHERE \"Id\" = {payoutId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (payout is null)
            {
                throw new KeyNotFoundException($"Заявку на виплату з ID '{payoutId}' не знайдено.");
            }

            if (payout.Status != PayoutRequestStatus.Pending)
            {
                throw new InvalidOperationException("Ця заявка на виплату вже була оброблена раніше.");
            }

            var partnerId = payout.PartnerId;
            var partner = await dbContext.Partners
                .FromSqlInterpolated($"SELECT * FROM \"Referrals\" WHERE \"Id\" = {partnerId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (partner is null)
            {
                throw new KeyNotFoundException($"Партнера з ID '{partnerId}' не знайдено.");
            }

            var now = DateTime.UtcNow;
            payout.Status = request.Status;
            payout.AdminComment = request.AdminComment?.Trim();
            payout.ProcessedAt = now;

            if (request.Status == PayoutRequestStatus.Rejected)
            {
                partner.TotalEarned += payout.Amount;
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            if (telegramBotClient is not null)
            {
                if (request.Status == PayoutRequestStatus.Completed)
                {
                    _ = telegramBotClient.SendPayoutCompletedNotificationAsync(
                        partner.TelegramUserId,
                        payout.Amount,
                        MaskCardNumber(payout.CardNumber),
                        CancellationToken.None);
                }
                else if (request.Status == PayoutRequestStatus.Rejected)
                {
                    _ = telegramBotClient.SendPayoutRejectedNotificationAsync(
                        partner.TelegramUserId,
                        payout.Amount,
                        request.AdminComment,
                        CancellationToken.None);
                }
            }

            return new AdminPayoutResponse(
                payout.Id,
                partner.Id,
                partner.TelegramUserId,
                partner.Name,
                partner.ReferralCode,
                payout.Amount,
                payout.CardNumber,
                payout.CardHolderName,
                payout.Status,
                payout.AdminComment,
                payout.CreatedAt,
                payout.ProcessedAt);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<PromoCodeResponse> CreatePromoCodeAsync(
        CreatePromoCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DiscountPercent <= 0m || request.DiscountPercent >= 100m)
        {
            throw new ArgumentException("Відсоток знижки має бути від 0.01% до 99.99%.", nameof(request));
        }

        var code = request.Code?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            code = "PROMO" + await GenerateUniqueReferralCodeAsync(cancellationToken);
        }
        else
        {
            var existsInReferrals = await dbContext.Partners
                .AnyAsync(p => p.ReferralCode.ToUpper() == code, cancellationToken);
            if (existsInReferrals)
            {
                throw new InvalidOperationException($"Код '{code}' вже використовується як реферальний код.");
            }

            var existsInPromos = await dbContext.PromoCodes
                .AnyAsync(p => p.Code.ToUpper() == code, cancellationToken);
            if (existsInPromos)
            {
                throw new InvalidOperationException($"Промокод '{code}' вже існує.");
            }
        }

        var maxUses = request.MaxUses is > 0 ? request.MaxUses : null;

        var promo = new PromoCode
        {
            Id = Guid.NewGuid(),
            Code = code,
            DiscountPercent = Math.Round(request.DiscountPercent, 2, MidpointRounding.AwayFromZero),
            MaxUses = maxUses,
            UsedCount = 0,
            IsActive = true,
            CreatedByTelegramUserId = request.CreatedByTelegramUserId,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.PromoCodes.Add(promo);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToPromoCodeResponse(promo);
    }

    public async Task<IReadOnlyList<PromoCodeResponse>> GetAllPromoCodesAsync(
        CancellationToken cancellationToken = default)
    {
        var list = await dbContext.PromoCodes
            .AsNoTracking()
            .OrderByDescending(p => p.IsActive)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return list.Select(MapToPromoCodeResponse).ToArray();
    }

    public async Task<bool> DeactivatePromoCodeAsync(
        Guid promoCodeId,
        CancellationToken cancellationToken = default)
    {
        var promo = await dbContext.PromoCodes
            .FirstOrDefaultAsync(p => p.Id == promoCodeId, cancellationToken);

        if (promo is null)
        {
            return false;
        }

        promo.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AdminBotOrderNotificationDto>> GetRecentOrdersForAdminBotAsync(
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var clampedLimit = Math.Clamp(limit, 1, 50);

        var orders = await dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Referrer)
            .Include(o => o.PromoCode)
            .OrderByDescending(o => o.CreatedAt)
            .Take(clampedLimit)
            .ToListAsync(cancellationToken);

        return orders.Select(MapToAdminBotOrderDto).ToArray();
    }

    public async Task<AdminBotOrdersPageDto> GetAdminBotOrdersPageAsync(
        string filter = "all",
        int page = 0,
        int pageSize = 5,
        CancellationToken cancellationToken = default)
    {
        var normalizedFilter = (filter ?? "all").Trim().ToLowerInvariant();
        var clampedSize = Math.Clamp(pageSize, 1, 20);
        var clampedPage = Math.Max(0, page);

        var localNow = DateTime.Now;
        var todayStartUtc = DateTime.SpecifyKind(localNow.Date, DateTimeKind.Local).ToUniversalTime();
        var yesterdayStartUtc = todayStartUtc.AddDays(-1);
        var weekStartUtc = todayStartUtc.AddDays(-7);

        var query = dbContext.Orders.AsNoTracking();

        query = normalizedFilter switch
        {
            "today" => query.Where(o => o.CreatedAt >= todayStartUtc),
            "yesterday" => query.Where(o => o.CreatedAt >= yesterdayStartUtc && o.CreatedAt < todayStartUtc),
            "7d" => query.Where(o => o.CreatedAt >= weekStartUtc),
            _ => query
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var totalRevenue = totalCount > 0
            ? await query
                .Where(o => o.OrderStatus != OrderStatus.Cancelled && o.OrderStatus != OrderStatus.Refused && o.OrderStatus != OrderStatus.Returned)
                .SumAsync(o => (decimal?)o.TotalPrice, cancellationToken) ?? 0m
            : 0m;

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)clampedSize));
        if (clampedPage >= totalPages)
        {
            clampedPage = totalPages - 1;
        }

        var orders = await query
            .Include(o => o.Items)
            .Include(o => o.Referrer)
            .Include(o => o.PromoCode)
            .OrderByDescending(o => o.CreatedAt)
            .Skip(clampedPage * clampedSize)
            .Take(clampedSize)
            .ToListAsync(cancellationToken);

        return new AdminBotOrdersPageDto(
            normalizedFilter,
            clampedPage,
            clampedSize,
            totalCount,
            totalRevenue,
            orders.Select(MapToAdminBotOrderDto).ToArray());
    }

    public async Task<AdminBotOrderNotificationDto?> GetAdminBotOrderByNumberAsync(
        int orderNumber,
        CancellationToken cancellationToken = default)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Referrer)
            .Include(o => o.PromoCode)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber, cancellationToken);

        return order is null ? null : MapToAdminBotOrderDto(order);
    }

    private static AdminBotOrderNotificationDto MapToAdminBotOrderDto(Order o) =>
        new(
            o.Id,
            o.OrderNumber,
            o.PublicId,
            $"{o.CustomerFirstName} {o.CustomerLastName}".Trim(),
            o.CustomerPhone,
            o.DeliveryCityName,
            o.DeliveryWarehouseName,
            o.PaymentMethod,
            o.OrderStatus,
            o.TotalPrice,
            o.DiscountAmount,
            o.PromoCode?.Code,
            o.PromoCode?.DiscountPercent,
            o.Referrer?.Name,
            o.Referrer?.ReferralCode,
            o.ReferralRewardAmount,
            o.CreatedAt,
            o.Items
                .Select(i => new AdminBotOrderItemDto(
                    i.ProductNameSnapshot,
                    i.Quantity,
                    i.UnitPrice,
                    i.UnitPrice * i.Quantity))
                .ToArray());

    private static PromoCodeResponse MapToPromoCodeResponse(PromoCode promo) =>
        new(
            promo.Id,
            promo.Code,
            promo.DiscountPercent,
            promo.MaxUses,
            promo.UsedCount,
            promo.IsActive,
            promo.CreatedByTelegramUserId,
            promo.CreatedAt);

    public static bool IsValidCardLuhn(string? digits)
    {
        if (string.IsNullOrWhiteSpace(digits) || digits.Length != 16)
        {
            return false;
        }

        var sum = 0;
        var alternate = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var ch = digits[i];
            if (!char.IsDigit(ch))
            {
                return false;
            }

            var n = ch - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                {
                    n -= 9;
                }
            }

            sum += n;
            alternate = !alternate;
        }

        return sum > 0 && sum % 10 == 0;
    }

    private static string MaskCardNumber(string card)
    {
        if (string.IsNullOrWhiteSpace(card) || card.Length < 16)
        {
            return card;
        }

        return $"{card[..4]} **** **** {card[^4..]}";
    }

    private async Task<string> GenerateUniqueReferralCodeAsync(CancellationToken cancellationToken)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var chars = new char[6];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }

            var candidate = new string(chars);
            var exists = await dbContext.Partners
                .AnyAsync(p => p.ReferralCode.ToUpper() == candidate, cancellationToken)
                || await dbContext.PromoCodes
                    .AnyAsync(p => p.Code.ToUpper() == candidate, cancellationToken);

            if (!exists)
            {
                return candidate;
            }
        }

        return "REF" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    }

    private static PartnerResponse MapToPartnerResponse(Partner partner, decimal pendingBalance)
    {
        return new PartnerResponse(
            partner.Id,
            partner.TelegramUserId,
            partner.Name,
            partner.ReferralCode,
            partner.CommissionPercent,
            partner.TotalEarned,
            partner.CreatedAt,
            pendingBalance,
            partner.TotalEarned,
            0,
            0,
            0m,
            partner.CardNumber,
            partner.CardHolderName);
    }
}
