using System.ComponentModel.DataAnnotations;

namespace EcommerceBackend.Application.DTOs.Finance;

public sealed record FinancialOverviewDto(
    decimal TotalRevenue,
    decimal TotalDropCost,
    decimal GrossProfit,
    decimal AdBudgetInvested,
    decimal AdSpendActual,
    decimal RemainingAdBudget,
    decimal NetProfitToDistribute);

public sealed record PartnerShareDto(
    Guid Id,
    Guid AdminId,
    string Username,
    string Name,
    string Role,
    decimal InvestedAmount,
    decimal SharePercent,
    decimal CurrentPayout,
    decimal Roi);

public sealed record PartnersSummaryResponse(
    FinancialOverviewDto FinancialOverview,
    IReadOnlyList<PartnerShareDto> Partners);

public sealed record CreateAdSpendRequest
{
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Сума витрат повинна бути більше нуля.")]
    public decimal Amount { get; init; }

    [Required, StringLength(100)]
    public required string Source { get; init; }

    public DateTime? Date { get; init; }

    public string? Note { get; init; }
}

public sealed record AdSpendResponse(
    Guid Id,
    decimal Amount,
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

    public DateTime? Date { get; init; }

    public string? Note { get; init; }
}

public sealed record PartnerDepositResponse(
    Guid Id,
    Guid AdminId,
    string AdminUsername,
    string AdminName,
    decimal Amount,
    DateTime Date,
    string? Note,
    DateTime CreatedAt);

public sealed record UpdatePartnerSettingsRequest
{
    public string? Role { get; init; }
    public decimal? FixedSharePercent { get; init; }
}
