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

        var partner = await FindPartnerByCodeAsync(referralCode, cancellationToken);
        if (partner is null)
        {
            return false;
        }

        var reward = Math.Round(
            order.TotalPrice * partner.CommissionPercent / 100m,
            2,
            MidpointRounding.AwayFromZero);

        order.ReferrerId = partner.Id;
        order.ReferralRewardAmount = reward;
        order.ReferralRewardStatus = ReferralRewardStatus.Pending;
        order.IsReferralRewardPaid = false;
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
                .FromSqlInterpolated($"SELECT * FROM \"Partners\" WHERE \"Id\" = {partnerId} FOR UPDATE")
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
            order.IsReferralRewardPaid = true;
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

            if (order.ReferrerId is null || ((order.ReferralRewardStatus == ReferralRewardStatus.None || order.ReferralRewardStatus == ReferralRewardStatus.Cancelled) && !order.IsReferralRewardPaid))
            {
                return null;
            }

            var partnerId = order.ReferrerId.Value;
            var partner = await dbContext.Partners
                .FromSqlInterpolated($"SELECT * FROM \"Partners\" WHERE \"Id\" = {partnerId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (partner is null)
            {
                return null;
            }

            if (order.ReferralRewardStatus == ReferralRewardStatus.Paid || order.IsReferralRewardPaid)
            {
                partner.TotalEarned = Math.Max(0m, partner.TotalEarned - order.ReferralRewardAmount);
            }

            var previousReward = order.ReferralRewardAmount;
            order.ReferralRewardStatus = ReferralRewardStatus.Cancelled;
            order.IsReferralRewardPaid = false;
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
        var pendingAmounts = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.ReferrerId.HasValue
                && partnerIds.Contains(o.ReferrerId.Value)
                && o.ReferralRewardStatus == ReferralRewardStatus.Pending)
            .GroupBy(o => o.ReferrerId!.Value)
            .Select(g => new { PartnerId = g.Key, PendingSum = g.Sum(x => x.ReferralRewardAmount) })
            .ToDictionaryAsync(x => x.PartnerId, x => x.PendingSum, cancellationToken);

        return partners.Select(p => MapToPartnerResponse(p, pendingAmounts.GetValueOrDefault(p.Id, 0m))).ToArray();
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

        var pendingSum = await dbContext.Orders
            .AsNoTracking()
            .Where(o => o.ReferrerId == partner.Id && o.ReferralRewardStatus == ReferralRewardStatus.Pending)
            .SumAsync(o => o.ReferralRewardAmount, cancellationToken);

        return MapToPartnerResponse(partner, pendingSum);
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

        var partner = new Partner
        {
            Id = Guid.NewGuid(),
            TelegramUserId = request.TelegramUserId,
            Name = name,
            ReferralCode = code,
            CommissionPercent = request.CommissionPercent,
            TotalEarned = 0m,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Partners.Add(partner);
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapToPartnerResponse(partner, 0m);
    }

    public async Task<ReferralCodeValidationResponse> ValidateCodeAsync(
        string? referralCode,
        CancellationToken cancellationToken = default)
    {
        var partner = await FindPartnerByCodeAsync(referralCode, cancellationToken);
        if (partner is null)
        {
            return new ReferralCodeValidationResponse(false, null, null);
        }

        return new ReferralCodeValidationResponse(true, partner.ReferralCode, partner.Name);
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
            partner.CreatedAt);
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

        var partner = new Partner
        {
            Id = Guid.NewGuid(),
            TelegramUserId = request.TelegramUserId,
            Name = name,
            ReferralCode = code,
            CommissionPercent = 5.0m,
            TotalEarned = 0m,
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
            partner.CreatedAt);
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
            cancelledCount);
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

        var cleanCard = new string(request.CardNumber.Where(char.IsDigit).ToArray());
        if (cleanCard.Length != 16)
        {
            throw new ArgumentException("Номер банківської картки повинен містити 16 цифр.", nameof(request));
        }

        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var partner = await dbContext.Partners
                .FromSqlInterpolated($"SELECT * FROM \"Partners\" WHERE \"TelegramUserId\" = {telegramUserId} FOR UPDATE")
                .FirstOrDefaultAsync(cancellationToken);

            if (partner is null)
            {
                throw new KeyNotFoundException($"Партнера з Telegram ID '{telegramUserId}' не знайдено.");
            }

            if (partner.TotalEarned < request.Amount)
            {
                throw new InvalidOperationException(
                    $"Недостатньо коштів на балансі. Доступно для виведення: {partner.TotalEarned:0.00} грн.");
            }

            partner.TotalEarned -= request.Amount;

            var payout = new PartnerPayoutRequest
            {
                Id = Guid.NewGuid(),
                PartnerId = partner.Id,
                Amount = request.Amount,
                CardNumber = cleanCard,
                CardHolderName = request.CardHolderName?.Trim(),
                Status = PayoutRequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            dbContext.PartnerPayoutRequests.Add(payout);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
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

        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var payout = await dbContext.PartnerPayoutRequests
                .FromSqlInterpolated($"SELECT * FROM \"PartnerPayoutRequests\" WHERE \"Id\" = {payoutId} FOR UPDATE")
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
                .FromSqlInterpolated($"SELECT * FROM \"Partners\" WHERE \"Id\" = {partnerId} FOR UPDATE")
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
                        cancellationToken);
                }
                else if (request.Status == PayoutRequestStatus.Rejected)
                {
                    _ = telegramBotClient.SendPayoutRejectedNotificationAsync(
                        partner.TelegramUserId,
                        payout.Amount,
                        request.AdminComment,
                        cancellationToken);
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
                .AnyAsync(p => p.ReferralCode.ToUpper() == candidate, cancellationToken);

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
            pendingBalance);
    }
}
