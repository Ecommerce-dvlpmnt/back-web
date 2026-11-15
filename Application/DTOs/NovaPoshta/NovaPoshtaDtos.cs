using System.Text.Json.Serialization;

namespace EcommerceBackend.Application.DTOs.NovaPoshta;

/// <summary>Відповідь API Нової Пошти у тому вигляді, в якому його очікує фронтенд.</summary>
public sealed class NovaPoshtaResult<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("data")]
    public List<T> Data { get; init; } = [];

    [JsonPropertyName("errors")]
    public List<string> Errors { get; init; } = [];
}

public sealed class NovaPoshtaCity
{
    [JsonPropertyName("Ref")]
    public string Ref { get; init; } = string.Empty;

    [JsonPropertyName("Description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("AreaDescription")]
    public string? AreaDescription { get; init; }
}

public sealed class NovaPoshtaWarehouse
{
    [JsonPropertyName("Ref")]
    public string Ref { get; init; } = string.Empty;

    [JsonPropertyName("Description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("Number")]
    public string? Number { get; init; }

    [JsonPropertyName("ShortAddress")]
    public string? ShortAddress { get; init; }

    [JsonPropertyName("Latitude")]
    public string? Latitude { get; init; }

    [JsonPropertyName("Longitude")]
    public string? Longitude { get; init; }

    [JsonPropertyName("CategoryOfWarehouse")]
    public string? CategoryOfWarehouse { get; init; }

    [JsonPropertyName("TypeOfWarehouse")]
    public string? TypeOfWarehouse { get; init; }

    [JsonPropertyName("TotalMaxWeightAllowed")]
    public string? TotalMaxWeightAllowed { get; init; }

    [JsonPropertyName("Schedule")]
    public Dictionary<string, string>? Schedule { get; init; }
}
