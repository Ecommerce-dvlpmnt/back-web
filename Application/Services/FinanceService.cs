using EcommerceBackend.Application.DTOs.Finance;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Application.Services;

public sealed class FinanceService(AppDbContext dbContext, ICurrencyRateService currencyRateService) : IFinanceService
{
    private static readonly OrderStatus[] ExcludedStatuses =
    [
        OrderStatus.Cancelled,
        OrderStatus.Refused,
        OrderStatus.Returned
    ];

    public async Task<PartnersSummaryResponse> GetPartnersSummaryAsync(CancellationToken cancellationToken = default)
    {
        var usdRate = await currencyRateService.GetUsdRateAsync(null, cancellationToken);
        if (usdRate <= 0m) usdRate = 45.32m;

        // 1. Fetch valid orders for revenue and drop cost
        var validOrders = await dbContext.Orders
            .AsNoTracking()
            .Where(o => !ExcludedStatuses.Contains(o.OrderStatus))
            .Select(o => new { o.TotalPrice, o.TotalProfit })
            .ToListAsync(cancellationToken);

        var totalRevenue = Math.Round(validOrders.Sum(o => o.TotalPrice), 2);
        var grossProfit = Math.Round(validOrders.Sum(o => o.TotalProfit), 2);
        var totalDropCost = Math.Round(totalRevenue - grossProfit, 2);

        var totalRevenueUsd = Math.Round(totalRevenue / usdRate, 2);
        var totalDropCostUsd = Math.Round(totalDropCost / usdRate, 2);
        var grossProfitUsd = Math.Round(grossProfit / usdRate, 2);

        // 2. Fetch ad budget and ad spends
        var deposits = await dbContext.PartnerDeposits
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var adSpends = await dbContext.AdSpends
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var adBudgetInvested = Math.Round(deposits.Sum(d => d.Amount), 2);
        var adBudgetInvestedUsd = Math.Round(deposits.Sum(d => d.AmountUsd > 0m ? d.AmountUsd : d.Amount / usdRate), 2);

        var adSpendActual = Math.Round(adSpends.Sum(s => s.Amount), 2);
        var adSpendActualUsd = Math.Round(adSpends.Sum(s => s.AmountUsd > 0m ? s.AmountUsd : s.Amount / usdRate), 2);

        var remainingAdBudget = Math.Round(adBudgetInvested - adSpendActual, 2);
        var remainingAdBudgetUsd = Math.Round(adBudgetInvestedUsd - adSpendActualUsd, 2);

        var netProfitToDistribute = Math.Max(0m, Math.Round(grossProfit - adSpendActual, 2));
        var netProfitToDistributeUsd = Math.Max(0m, Math.Round(grossProfitUsd - adSpendActualUsd, 2));

        var financialOverview = new FinancialOverviewDto(
            UsdRate: usdRate,
            TotalRevenue: totalRevenue,
            TotalRevenueUsd: totalRevenueUsd,
            TotalDropCost: totalDropCost,
            TotalDropCostUsd: totalDropCostUsd,
            GrossProfit: grossProfit,
            GrossProfitUsd: grossProfitUsd,
            AdBudgetInvested: adBudgetInvested,
            AdBudgetInvestedUsd: adBudgetInvestedUsd,
            AdSpendActual: adSpendActual,
            AdSpendActualUsd: adSpendActualUsd,
            RemainingAdBudget: remainingAdBudget,
            RemainingAdBudgetUsd: remainingAdBudgetUsd,
            NetProfitToDistribute: netProfitToDistribute,
            NetProfitToDistributeUsd: netProfitToDistributeUsd);

        // 3. Fetch partners (admins with role Admin)
        var admins = await dbContext.Admins
            .AsNoTracking()
            .Include(a => a.Deposits)
            .Where(a => a.Role == "Admin" && a.IsActive)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        var partnerShares = new List<PartnerShareDto>(admins.Count);

        foreach (var admin in admins)
        {
            var investedAmount = Math.Round(admin.Deposits.Sum(d => d.Amount), 2);
            var investedAmountUsd = Math.Round(admin.Deposits.Sum(d => d.AmountUsd > 0m ? d.AmountUsd : d.Amount / usdRate), 2);

            decimal sharePercent;
            if (admin.FixedSharePercent.HasValue && admin.FixedSharePercent.Value > 0)
            {
                sharePercent = admin.FixedSharePercent.Value;
            }
            else if (adBudgetInvestedUsd > 0)
            {
                // In dynamic share, compare USD values to protect against inflation when someone deposited earlier!
                sharePercent = Math.Round((investedAmountUsd / adBudgetInvestedUsd) * 100m, 2);
            }
            else if (adBudgetInvested > 0)
            {
                sharePercent = Math.Round((investedAmount / adBudgetInvested) * 100m, 2);
            }
            else
            {
                sharePercent = admins.Count > 0 ? Math.Round(100m / admins.Count, 2) : 0m;
            }

            var currentPayout = Math.Round(netProfitToDistribute * (sharePercent / 100m), 2);
            var currentPayoutUsd = Math.Round(netProfitToDistributeUsd * (sharePercent / 100m), 2);

            decimal roi = 0m;
            if (investedAmount > 0)
            {
                roi = Math.Round(((currentPayout - investedAmount) / investedAmount) * 100m, 1);
            }
            else if (currentPayout > 0)
            {
                roi = 100m;
            }

            partnerShares.Add(new PartnerShareDto(
                Id: admin.Id,
                AdminId: admin.Id,
                Username: admin.Username,
                Name: admin.Name,
                Role: admin.PartnerRole ?? "Admin / Partner",
                InvestedAmount: investedAmount,
                InvestedAmountUsd: investedAmountUsd,
                SharePercent: sharePercent,
                CurrentPayout: currentPayout,
                CurrentPayoutUsd: currentPayoutUsd,
                Roi: roi));
        }

        return new PartnersSummaryResponse(financialOverview, partnerShares);
    }

    public async Task<AdSpendResponse> AddAdSpendAsync(
        CreateAdSpendRequest request,
        Guid? adminId = null,
        CancellationToken cancellationToken = default)
    {
        var targetDate = request.Date.HasValue
            ? (request.Date.Value.Kind == DateTimeKind.Utc ? request.Date.Value : DateTime.SpecifyKind(request.Date.Value, DateTimeKind.Utc))
            : DateTime.UtcNow;

        var rate = request.CustomExchangeRate.HasValue && request.CustomExchangeRate.Value > 0
            ? request.CustomExchangeRate.Value
            : await currencyRateService.GetUsdRateAsync(targetDate, cancellationToken);
        if (rate <= 0m) rate = 45.32m;

        decimal amountUah;
        decimal amountUsd;

        if (string.Equals(request.Currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            amountUsd = Math.Round(request.Amount, 2);
            amountUah = Math.Round(amountUsd * rate, 2);
        }
        else
        {
            amountUah = Math.Round(request.Amount, 2);
            amountUsd = Math.Round(amountUah / rate, 2);
        }

        var adSpend = new AdSpend
        {
            Id = Guid.NewGuid(),
            Amount = amountUah,
            AmountUsd = amountUsd,
            ExchangeRate = Math.Round(rate, 4),
            Source = request.Source.Trim(),
            Date = targetDate,
            Note = request.Note?.Trim(),
            RecordedByAdminId = adminId,
            CreatedAt = DateTime.UtcNow
        };

        dbContext.AdSpends.Add(adSpend);
        await dbContext.SaveChangesAsync(cancellationToken);

        string? adminName = null;
        if (adminId.HasValue)
        {
            var admin = await dbContext.Admins.FindAsync([adminId.Value], cancellationToken);
            adminName = admin?.Username ?? admin?.Name;
        }

        return new AdSpendResponse(
            Id: adSpend.Id,
            Amount: adSpend.Amount,
            AmountUsd: adSpend.AmountUsd,
            ExchangeRate: adSpend.ExchangeRate,
            Source: adSpend.Source,
            Date: adSpend.Date,
            Note: adSpend.Note,
            RecordedByAdminId: adSpend.RecordedByAdminId,
            RecordedByAdminName: adminName,
            CreatedAt: adSpend.CreatedAt);
    }

    public async Task<IReadOnlyList<AdSpendResponse>> GetAdSpendsAsync(CancellationToken cancellationToken = default)
    {
        var items = await dbContext.AdSpends
            .AsNoTracking()
            .Include(s => s.RecordedByAdmin)
            .OrderByDescending(s => s.Date)
            .ThenByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        return items.Select(s => new AdSpendResponse(
            Id: s.Id,
            Amount: s.Amount,
            AmountUsd: s.AmountUsd,
            ExchangeRate: s.ExchangeRate,
            Source: s.Source,
            Date: s.Date,
            Note: s.Note,
            RecordedByAdminId: s.RecordedByAdminId,
            RecordedByAdminName: s.RecordedByAdmin != null ? (s.RecordedByAdmin.Username ?? s.RecordedByAdmin.Name) : null,
            CreatedAt: s.CreatedAt)).ToList();
    }

    public async Task<bool> DeleteAdSpendAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await dbContext.AdSpends.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (item is null) return false;

        dbContext.AdSpends.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PartnerDepositResponse> AddDepositAsync(
        CreateDepositRequest request,
        Guid? currentAdminId = null,
        CancellationToken cancellationToken = default)
    {
        var targetAdminId = request.AdminId ?? currentAdminId;
        if (!targetAdminId.HasValue)
        {
            throw new ArgumentException("Необхідно вказати ID адміністратора/партнера.");
        }

        var admin = await dbContext.Admins.FirstOrDefaultAsync(a => a.Id == targetAdminId.Value, cancellationToken);
        if (admin is null)
        {
            throw new KeyNotFoundException($"Партнера з ID '{targetAdminId}' не знайдено.");
        }

        var targetDate = request.Date.HasValue
            ? (request.Date.Value.Kind == DateTimeKind.Utc ? request.Date.Value : DateTime.SpecifyKind(request.Date.Value, DateTimeKind.Utc))
            : DateTime.UtcNow;

        var rate = request.CustomExchangeRate.HasValue && request.CustomExchangeRate.Value > 0
            ? request.CustomExchangeRate.Value
            : await currencyRateService.GetUsdRateAsync(targetDate, cancellationToken);
        if (rate <= 0m) rate = 45.32m;

        decimal amountUah;
        decimal amountUsd;

        if (string.Equals(request.Currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            amountUsd = Math.Round(request.Amount, 2);
            amountUah = Math.Round(amountUsd * rate, 2);
        }
        else
        {
            amountUah = Math.Round(request.Amount, 2);
            amountUsd = Math.Round(amountUah / rate, 2);
        }

        var deposit = new PartnerDeposit
        {
            Id = Guid.NewGuid(),
            AdminId = admin.Id,
            Amount = amountUah,
            AmountUsd = amountUsd,
            ExchangeRate = Math.Round(rate, 4),
            Date = targetDate,
            Note = request.Note?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        dbContext.PartnerDeposits.Add(deposit);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PartnerDepositResponse(
            Id: deposit.Id,
            AdminId: admin.Id,
            AdminUsername: admin.Username,
            AdminName: admin.Name,
            Amount: deposit.Amount,
            AmountUsd: deposit.AmountUsd,
            ExchangeRate: deposit.ExchangeRate,
            Date: deposit.Date,
            Note: deposit.Note,
            CreatedAt: deposit.CreatedAt);
    }

    public async Task<IReadOnlyList<PartnerDepositResponse>> GetDepositsAsync(CancellationToken cancellationToken = default)
    {
        var items = await dbContext.PartnerDeposits
            .AsNoTracking()
            .Include(d => d.Admin)
            .OrderByDescending(d => d.Date)
            .ThenByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

        return items.Select(d => new PartnerDepositResponse(
            Id: d.Id,
            AdminId: d.AdminId,
            AdminUsername: d.Admin.Username,
            AdminName: d.Admin.Name,
            Amount: d.Amount,
            AmountUsd: d.AmountUsd,
            ExchangeRate: d.ExchangeRate,
            Date: d.Date,
            Note: d.Note,
            CreatedAt: d.CreatedAt)).ToList();
    }

    public async Task<bool> UpdatePartnerSettingsAsync(
        Guid adminId,
        UpdatePartnerSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var admin = await dbContext.Admins.FirstOrDefaultAsync(a => a.Id == adminId, cancellationToken);
        if (admin is null) return false;

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            admin.PartnerRole = request.Role.Trim();
        }

        if (request.FixedSharePercent.HasValue)
        {
            admin.FixedSharePercent = request.FixedSharePercent.Value;
        }

        admin.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
