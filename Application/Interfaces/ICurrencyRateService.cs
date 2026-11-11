namespace EcommerceBackend.Application.Interfaces;

public interface ICurrencyRateService
{
    Task<decimal> GetUsdRateAsync(DateTime? date = null, CancellationToken cancellationToken = default);

    Task<CurrencyRateInfo> GetCurrentRatesAsync(CancellationToken cancellationToken = default);
}

public sealed record CurrencyRateInfo(
    string Currency,
    decimal Rate,
    DateTime Date,
    string Source);
