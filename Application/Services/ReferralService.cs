using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Application.Services;

public sealed class ReferralService(
    AppDbContext dbContext,
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

        order.ReferrerId = partner.Id;
        order.ReferralRewardAmount = 0m;
        order.IsReferralRewardPaid = false;
        return true;
    }

    public async Task<ReferralRewardResult?> ProcessPaidOrderAsync(
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

            if (order.PaymentStatus != PaymentStatus.Paid
                || order.ReferrerId is null
                || order.IsReferralRewardPaid)
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

            var reward = Math.Round(
                order.TotalPrice * partner.CommissionPercent / 100m,
                2,
                MidpointRounding.AwayFromZero);

            partner.TotalEarned += reward;
            order.ReferralRewardAmount = reward;
            order.IsReferralRewardPaid = true;
            order.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            logger?.LogInformation(
                "Реферальну винагороду {Reward} грн нараховано партнеру {PartnerId} за замовлення #{OrderNumber}.",
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
                partner.TotalEarned);
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

        return partners.Select(MapToPartnerResponse).ToArray();
    }

    public async Task<PartnerResponse?> GetPartnerByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var partner = await dbContext.Partners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        return partner is null ? null : MapToPartnerResponse(partner);
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

        return MapToPartnerResponse(partner);
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

    private static PartnerResponse MapToPartnerResponse(Partner partner)
    {
        return new PartnerResponse(
            partner.Id,
            partner.TelegramUserId,
            partner.Name,
            partner.ReferralCode,
            partner.CommissionPercent,
            partner.TotalEarned,
            partner.CreatedAt);
    }
}
