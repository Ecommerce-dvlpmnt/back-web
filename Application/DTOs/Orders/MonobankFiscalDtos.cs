using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EcommerceBackend.Application.DTOs.Orders;

/// <summary>Знижка або надбавка для товару чи всього кошика.</summary>
public sealed record MonobankDiscountDto
{
    /// <summary>Тип: "discount" (знижка) або "extra" (надбавка / доставка).</summary>
    [JsonPropertyName("type")]
    [Required]
    public string Type { get; init; } = "discount";

    /// <summary>Режим: "percent" (у відсотках) або "value" (фіксована сума в копійках).</summary>
    [JsonPropertyName("mode")]
    [Required]
    public string Mode { get; init; } = "percent";

    /// <summary>Значення (наприклад 10 для 10%, або 500 для 5 грн).</summary>
    [JsonPropertyName("value")]
    [Range(typeof(decimal), "0.01", "100000000")]
    public decimal Value { get; init; }
}

/// <summary>Позиція кошика для формування фіскального чека пРРО в Monobank.</summary>
public sealed record MonobankFiscalItemDto
{
    /// <summary>Назва товару або послуги.</summary>
    [JsonPropertyName("name")]
    [Required, StringLength(255, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>Кількість (може бути дробовою, наприклад 1.5 для ваги).</summary>
    [JsonPropertyName("qty")]
    [Range(typeof(decimal), "0.001", "100000")]
    public decimal Qty { get; init; } = 1m;

    /// <summary>Ціна за 1 шт / одиницю в копійках (наприклад 6000 = 60.00 грн).</summary>
    [JsonPropertyName("sum")]
    [Range(1, 100_000_000)]
    public long Sum { get; init; }

    /// <summary>Загальна вартість позиції до знижки кошика: qty * sum (у копійках).</summary>
    [JsonPropertyName("total")]
    [Range(0, 1_000_000_000)]
    public long Total { get; init; }

    /// <summary>Внутрішній код товару / артикул (наприклад JD-0001).</summary>
    [JsonPropertyName("code")]
    [StringLength(64)]
    public string? Code { get; init; }

    /// <summary>Штрихкод (опційно).</summary>
    [JsonPropertyName("barcode")]
    [StringLength(64)]
    public string? Barcode { get; init; }

    /// <summary>Податкові групи пРРО: [0]=без ПДВ, [1]=20% ПДВ, [2]=7% тощо.</summary>
    [JsonPropertyName("tax")]
    public IReadOnlyList<int>? Tax { get; init; } = [0];

    /// <summary>Індивідуальні знижки на цей конкретний товар.</summary>
    [JsonPropertyName("discounts")]
    public IReadOnlyList<MonobankDiscountDto>? Discounts { get; init; }

    /// <summary>Одиниця виміру (наприклад "шт.", "кг").</summary>
    [JsonPropertyName("unit")]
    [StringLength(20)]
    public string? Unit { get; init; } = "шт.";

    /// <summary>URL фотографії товару для сторінки оплати Monobank (опційно).</summary>
    [JsonPropertyName("icon")]
    [StringLength(1024)]
    public string? Icon { get; init; }
}

/// <summary>Запит на створення платежу з фіскальним кошиком, знижками та ПРРО (тільки для адміністраторів / внутрішнього виклику).</summary>
public sealed record CreateFiscalInvoiceRequest
{
    /// <summary>Перелік товарів у кошику.</summary>
    [Required, MinLength(1)]
    public required IReadOnlyList<MonobankFiscalItemDto> BasketOrder { get; init; }

    /// <summary>Знижки або надбавки на рівні всього кошика (наприклад знижка 10% або доставка +50 грн).</summary>
    public IReadOnlyList<MonobankDiscountDto>? CartDiscounts { get; init; }

    /// <summary>Призначення платежу / опис (за замовчуванням: "Оплата замовлення").</summary>
    [Required, StringLength(255, MinimumLength = 1)]
    public string Destination { get; init; } = "Оплата замовлення";

    /// <summary>Унікальний референс (наприклад номер чека/замовлення).</summary>
    [StringLength(64)]
    public string? Reference { get; init; }

    /// <summary>Куди повертати покупця після оплати.</summary>
    public string? RedirectUrl { get; init; }

    /// <summary>Адреса вебхука для отримання фінального статусу.</summary>
    public string? WebhookUrl { get; init; }
}

/// <summary>Модель фіскального чека з GET /api/merchant/invoice/fiscal-checks.</summary>
public sealed record MonobankFiscalCheckItem
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; } // "sale" or "return"

    [JsonPropertyName("fiscalizationSource")]
    public string? FiscalizationSource { get; init; }

    [JsonPropertyName("statusDescription")]
    public string? StatusDescription { get; init; }

    /// <summary>Пряме посилання на чек на сайті податкової (tax.gov.ua).</summary>
    [JsonPropertyName("taxUrl")]
    public string? TaxUrl { get; init; }

    /// <summary>Base64-encoded PDF файл чека.</summary>
    [JsonPropertyName("file")]
    public string? FileBase64 { get; init; }
}

/// <summary>Відповідь списку фіскальних чеків.</summary>
public sealed record MonobankFiscalChecksResponse
{
    [JsonPropertyName("checks")]
    public IReadOnlyList<MonobankFiscalCheckItem>? Checks { get; init; }
}
