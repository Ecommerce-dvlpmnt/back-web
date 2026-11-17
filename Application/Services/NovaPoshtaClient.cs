using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EcommerceBackend.Application.DTOs.NovaPoshta;
using EcommerceBackend.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace EcommerceBackend.Application.Services;

public sealed class NovaPoshtaClient(
    HttpClient httpClient,
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<NovaPoshtaClient> logger) : INovaPoshtaClient
{
    private const string ModelName = "Address";
    private static readonly TimeSpan WarehousesCacheTtl = TimeSpan.FromMinutes(30);

    public async Task<NovaPoshtaResult<NovaPoshtaCity>> GetCitiesAsync(
        string search,
        CancellationToken cancellationToken = default)
    {
        var request = new NovaPoshtaRequest<CitySearchProperties>(
            ApiKey,
            ModelName,
            "getCities",
            new CitySearchProperties(search.Trim(), "15"));

        return await PostAsync<NovaPoshtaCity, CitySearchProperties>(request, cancellationToken);
    }

    public async Task<NovaPoshtaResult<NovaPoshtaWarehouse>> GetWarehousesAsync(
        string cityRef,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"novaposhta:warehouses:{cityRef}";
        if (cache.TryGetValue(cacheKey, out var cached) &&
            cached is NovaPoshtaResult<NovaPoshtaWarehouse> cachedResult)
        {
            return cachedResult;
        }

        var request = new NovaPoshtaRequest<WarehouseQueryProperties>(
            ApiKey,
            ModelName,
            "getWarehouses",
            new WarehouseQueryProperties(cityRef));

        var result = await PostAsync<NovaPoshtaWarehouse, WarehouseQueryProperties>(
            request,
            cancellationToken);

        if (result.Success)
        {
            cache.Set(cacheKey, result, WarehousesCacheTtl);
        }

        return result;
    }

    private string BaseUrl =>
        configuration["NovaPoshta:BaseUrl"]
        ?? configuration["NOVAPOSHTA_BASEURL"]
        ?? "https://api.novaposhta.ua/v2.0/json/";

    private string ApiKey
    {
        get
        {
            var value = configuration["NovaPoshta:ApiKey"]
                ?? configuration["NOVAPOSHTA_APIKEY"];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    "Налаштування 'NovaPoshta:ApiKey' не задане. Вкажіть ключ у файлі конфігурації або змінній середовища 'NovaPoshta__ApiKey' чи 'NOVAPOSHTA_APIKEY'.");
            }

            return value;
        }
    }

    private async Task<NovaPoshtaResult<T>> PostAsync<T, TProperties>(
        NovaPoshtaRequest<TProperties> request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            BaseUrl,
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<NovaPoshtaResult<T>>(cancellationToken);
        if (result is null)
        {
            logger.LogWarning(
                "Нова Пошта повернула порожню відповідь для методу {Method}.",
                request.CalledMethod);

            return new NovaPoshtaResult<T>
            {
                Success = false,
                Errors = ["Порожня відповідь Нової Пошти."]
            };
        }

        if (!result.Success)
        {
            logger.LogWarning(
                "Нова Пошта повернула помилку для методу {Method}: {Errors}.",
                request.CalledMethod,
                string.Join("; ", result.Errors));
        }

        return result;
    }

    private sealed record NovaPoshtaRequest<TProperties>(
        [property: JsonPropertyName("apiKey")] string ApiKey,
        [property: JsonPropertyName("modelName")] string ModelName,
        [property: JsonPropertyName("calledMethod")] string CalledMethod,
        [property: JsonPropertyName("methodProperties")] TProperties MethodProperties);

    private sealed record CitySearchProperties(
        [property: JsonPropertyName("FindByString")] string FindByString,
        [property: JsonPropertyName("Limit")] string Limit);

    private sealed record WarehouseQueryProperties(
        [property: JsonPropertyName("CityRef")] string CityRef);
}
