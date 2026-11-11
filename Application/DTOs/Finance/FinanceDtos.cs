using System.ComponentModel.DataAnnotations;

namespace EcommerceBackend.Application.DTOs.Finance;

public sealed record FinancialOverviewDto(
    decimal UsdRate,
    decimal TotalRevenue,
    decimal TotalRevenueUsd,
    decimal TotalDropCost,
    decimal TotalDropCostUsd,
    decimal GrossProfit,
    decimal GrossProfitUsd,
    decimal AdBudgetInvested,
    decimal AdBudgetInvestedUsd,
    decimal AdSpendActual,
    decimal AdSpendActualUsd,
    decimal RemainingAdBudget,
    decimal RemainingAdBudgetUsd,
    decimal NetProfitToDistribute,
    decimal NetProfitToDistributeUsd);

public sealed record PartnerShareDto(
    Guid Id,
    Guid AdminId,
    string Username,
    string Name,
    string Role,
    decimal InvestedAmount,
    decimal InvestedAmountUsd,
    decimal SharePercent,
    decimal CurrentPayout,
    decimal CurrentPayoutUsd,
    decimal Roi);

public sealed record PartnersSummaryResponse(
    FinancialOverviewDto FinancialOverview,
    IReadOnlyList<PartnerShareDto> Partners);

public sealed record CreateAdSpendRequest
{
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Сума витрат повинна бути більше нуля.")]
    public decimal Amount { get; init; }

    public string Currency { get; init; } = "UAH"; // "UAH" or "USD"

    public decimal? CustomExchangeRate { get; init; }

    [Required, StringLength(100)]
    public required string Source { get; init; }

    public DateTime? Date { get; init; }

    public string? Note { get; init; }
}

public sealed record AdSpendResponse(
    Guid Id,
    decimal Amount,
    decimal AmountUsd,
    decimal ExchangeRate,
    string Source,
    DateTime Date,
    string? Note,
    Guid? RecordedByAdminId,
    string? RecordedByAdminName,
    DateTime CreatedAt);

public sealed record CreateDepositRequest
{
    public Guid? AdminId { get; init; }

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Сума внеску повинна бути більше нуля.")]
    public decimal Amount { get; init; }

    public string Currency { get; init; } = "UAH"; // "UAH" or "USD"

    public decimal? CustomExchangeRate { get; init; }

    public DateTime? Date { get; init; }

    public string? Note { get; init; }
}

public sealed record PartnerDepositResponse(
    Guid Id,
    Guid AdminId,
    string AdminUsername,
    string AdminName,
    decimal Amount,
    decimal AmountUsd,
    decimal ExchangeRate,
    DateTime Date,
    string? Note,
    DateTime CreatedAt);

public sealed record UpdatePartnerSettingsRequest
{
    public string? Role { get; init; }
    public decimal? FixedSharePercent { get; init; }
}
