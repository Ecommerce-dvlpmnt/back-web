using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using EcommerceBackend.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace EcommerceBackend.Application.Services;

public sealed class NbuCurrencyRateService(HttpClient httpClient, ILogger<NbuCurrencyRateService> logger) : ICurrencyRateService
{
    private const decimal FallbackUsdRate = 45.32m;
    private static readonly ConcurrentDictionary<string, (decimal Rate, DateTime CachedAt)> Cache = new();

    public async Task<decimal> GetUsdRateAsync(DateTime? date = null, CancellationToken cancellationToken = default)
    {
        var targetDate = date?.Date ?? DateTime.UtcNow.Date;
        var cacheKey = targetDate.ToString("yyyyMMdd");

        // Cache historical dates permanently, and today's rate for 1 hour
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            if (targetDate < DateTime.UtcNow.Date || DateTime.UtcNow - cached.CachedAt < TimeSpan.FromHours(1))
            {
                return cached.Rate;
            }
        }

        try
        {
            var url = targetDate == DateTime.UtcNow.Date
                ? "https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?valcode=USD&json"
                : $"https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?valcode=USD&date={cacheKey}&json";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await httpClient.GetAsync(url, cts.Token);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cts.Token);
                var items = JsonSerializer.Deserialize<List<NbuExchangeItem>>(json);
                if (items != null && items.Count > 0 && items[0].Rate > 0)
                {
                    var rate = Math.Round(items[0].Rate, 4);
                    Cache[cacheKey] = (rate, DateTime.UtcNow);
                    return rate;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не вдалося отримати офіційний курс НБУ для дати {Date}. Використовується резервний курс {Rate}.", targetDate, FallbackUsdRate);
        }

        // Return cached rate if exists, otherwise fallback
        if (Cache.TryGetValue(cacheKey, out var fallbackCached))
        {
            return fallbackCached.Rate;
        }

        return FallbackUsdRate;
    }

    public async Task<CurrencyRateInfo> GetCurrentRatesAsync(CancellationToken cancellationToken = default)
    {
        var rate = await GetUsdRateAsync(null, cancellationToken);
        return new CurrencyRateInfo(
            Currency: "USD",
            Rate: rate,
            Date: DateTime.UtcNow,
            Source: "Національний Банк України (НБУ)");
    }

    private sealed class NbuExchangeItem
    {
        [JsonPropertyName("rate")]
        public decimal Rate { get; set; }

        [JsonPropertyName("cc")]
        public string? CurrencyCode { get; set; }

        [JsonPropertyName("exchangedate")]
        public string? ExchangeDate { get; set; }
    }
}
