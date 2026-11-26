using System.Globalization;
using System.Text;
using System.Text.Json;
using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.DTOs.Seo;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace EcommerceBackend.Application.Services;

public sealed class SeoService(
    IProductRepository productRepository,
    ICategoryRepository categoryRepository,
    IMemoryCache memoryCache,
    IConfiguration configuration) : ISeoService
{
    private const string LlmsTxtCacheKey = "seo:llms-txt:v1";
    private const string LlmsFullTxtCacheKey = "seo:llms-full-txt:v1";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<string> GetLlmsTxtAsync(CancellationToken cancellationToken = default)
    {
        if (memoryCache.TryGetValue(LlmsTxtCacheKey, out string? cached) && cached is not null)
            return cached;

        var siteUrl = ResolveSiteUrl();
        var categories = (await categoryRepository.GetAllAsync(onlyActive: true, cancellationToken))
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToList();
        var products = await LoadAllActiveProductsAsync(cancellationToken);
        var inStockProducts = products.Where(p => p.Stock > 0).ToList();

        var sb = new StringBuilder(4096);
        sb.AppendLine($"# {SeoMetaBuilder.BrandName}");
        sb.AppendLine();
        sb.AppendLine("> Український інтернет-магазин преміальних штучних литих ялинок, різдвяних вінків ручної роботи, хвойних та світлодіодних гірлянд і святкового декору для дому.");
        sb.AppendLine();
        sb.AppendLine("## Ключова інформація про магазин");
        sb.AppendLine($"- Офіційний сайт: {siteUrl}");
        sb.AppendLine("- Країна та географія доставки: Україна (відправка у всі відділення та поштомати Нової Пошти за 1–2 дні).");
        sb.AppendLine("- Способи оплати: післяплата (оплата при отриманні після огляду у відділенні Нової Пошти), онлайн-оплата карткою через Monobank (Visa, Mastercard, Apple Pay, Google Pay), безготівковий переказ за реквізитами IBAN ФОП.");
        sb.AppendLine("- Обмін та повернення: 14 днів згідно із Законом України «Про захист прав споживачів».");
        sb.AppendLine("- Матеріали: сертифікована екологічна 100% лита хвоя без хімічного запаху, безпечна для дітей і домашніх тварин, термін служби понад 10 років.");
        sb.AppendLine($"- Активних товарів у каталозі: {products.Count} (у наявності: {inStockProducts.Count}).");
        sb.AppendLine();

        if (categories.Count > 0)
        {
            sb.AppendLine("## Категорії каталогу");
            foreach (var category in categories)
            {
                var catUrl = $"{siteUrl}/catalog/{Uri.EscapeDataString(category.Slug)}";
                var catDesc = SeoMetaBuilder.BuildCategoryMetaDescription(category.Name, category.MetaDescription);
                sb.AppendLine($"- [{category.Name}]({catUrl}): {catDesc}");
            }
            sb.AppendLine();
        }

        if (inStockProducts.Count > 0)
        {
            sb.AppendLine("## Популярні товари в наявності");
            var featured = categories.Count > 0
                ? categories
                    .SelectMany(cat => inStockProducts
                        .Where(p => p.CategoryId == cat.Id)
                        .OrderByDescending(p => p.UpdatedAt)
                        .Take(4))
                    .DistinctBy(p => p.Id)
                    .Take(24)
                    .ToList()
                : inStockProducts.Take(20).ToList();

            foreach (var product in featured)
            {
                var productUrl = $"{siteUrl}/product/{Uri.EscapeDataString(product.Slug)}";
                var priceFormatted = FormatPriceUah(product.Price);
                var shortDesc = SeoMetaBuilder.TruncateAtWordBoundary(
                    SeoMetaBuilder.StripHtmlAndNormalize(product.Description),
                    120);
                var summaryPart = string.IsNullOrEmpty(shortDesc) ? string.Empty : $" — {shortDesc}";
                sb.AppendLine($"- [{product.Name}]({productUrl}): {priceFormatted} (SKU: {product.Sku}){summaryPart}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Корисні сторінки та машиночитані фіди");
        sb.AppendLine($"- [Повний каталог товарів для AI (llms-full.txt)]({siteUrl}/llms-full.txt)");
        sb.AppendLine($"- [Карта сайту XML]({siteUrl}/sitemap.xml)");
        sb.AppendLine($"- [Товарний фід Google Merchant XML]({siteUrl}/feed.xml)");
        sb.AppendLine($"- [Оплата і доставка]({siteUrl}/delivery-and-payment)");
        sb.AppendLine($"- [Обмін та повернення]({siteUrl}/returns)");
        sb.AppendLine($"- [Контакти]({siteUrl}/contacts)");

        var result = sb.ToString();
        memoryCache.Set(LlmsTxtCacheKey, result, CacheDuration);
        return result;
    }

    public async Task<string> GetLlmsFullTxtAsync(CancellationToken cancellationToken = default)
    {
        if (memoryCache.TryGetValue(LlmsFullTxtCacheKey, out string? cached) && cached is not null)
            return cached;

        var siteUrl = ResolveSiteUrl();
        var categories = (await categoryRepository.GetAllAsync(onlyActive: true, cancellationToken))
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToList();
        var products = await LoadAllActiveProductsAsync(cancellationToken);
        var categoryLookup = categories.ToDictionary(c => c.Id, c => c);

        var sb = new StringBuilder(Math.Max(8192, products.Count * 320));
        sb.AppendLine($"# {SeoMetaBuilder.BrandName} — Повний каталог та довідник магазину");
        sb.AppendLine();
        sb.AppendLine($"> Актуальна база знань про асортимент, ціни, наявність та умови замовлення інтернет-магазину {SeoMetaBuilder.BrandName} ({siteUrl}).");
        sb.AppendLine();
        sb.AppendLine("## Часті запитання (FAQ)");
        foreach (var (question, answer) in GetStoreFaqItems())
        {
            sb.AppendLine($"### {question}");
            sb.AppendLine(answer);
            sb.AppendLine();
        }

        sb.AppendLine("## Повний перелік товарів за категоріями");
        sb.AppendLine();

        var grouped = products
            .GroupBy(p => p.CategoryId)
            .OrderBy(g => categoryLookup.TryGetValue(g.Key, out var cat) ? cat.SortOrder : int.MaxValue);

        foreach (var group in grouped)
        {
            var categoryName = categoryLookup.TryGetValue(group.Key, out var category)
                ? category.Name
                : group.First().Category?.Name ?? "Інші товари";
            var categoryUrl = category is not null
                ? $"{siteUrl}/catalog/{Uri.EscapeDataString(category.Slug)}"
                : siteUrl;

            sb.AppendLine($"### {categoryName} ({categoryUrl})");
            sb.AppendLine();

            foreach (var product in group.OrderByDescending(p => p.Stock > 0).ThenBy(p => p.Price))
            {
                var productUrl = $"{siteUrl}/product/{Uri.EscapeDataString(product.Slug)}";
                var availability = product.Stock > 0 ? "В наявності" : "Немає в наявності";
                var priceStr = FormatPriceUah(product.Price);
                var oldPriceStr = product.OldPrice.HasValue && product.OldPrice.Value > product.Price
                    ? $" (стара ціна: {FormatPriceUah(product.OldPrice.Value)})"
                    : string.Empty;

                sb.AppendLine($"#### [{product.Name}]({productUrl})");
                sb.AppendLine($"- Артикул (SKU): {product.Sku}");
                sb.AppendLine($"- Ціна: {priceStr}{oldPriceStr}");
                sb.AppendLine($"- Статус: {availability}");

                var attrsText = FormatAttributesInline(product.Attributes);
                if (!string.IsNullOrEmpty(attrsText))
                    sb.AppendLine($"- Характеристики: {attrsText}");

                var cleanDesc = SeoMetaBuilder.StripHtmlAndNormalize(product.Description);
                if (!string.IsNullOrEmpty(cleanDesc))
                    sb.AppendLine($"- Опис: {SeoMetaBuilder.TruncateAtWordBoundary(cleanDesc, 400)}");

                sb.AppendLine();
            }
        }

        var result = sb.ToString();
        memoryCache.Set(LlmsFullTxtCacheKey, result, CacheDuration);
        return result;
    }

    public SeoStructuredDataResponse GetHomeStructuredData()
    {
        var siteUrl = ResolveSiteUrl();
        var metaTitle = $"{SeoMetaBuilder.BrandName} — литі ялинки, різдвяні вінки та святковий декор";
        var metaDescription =
            "Купити штучні литі ялинки, різдвяні вінки ручної роботи та хвойні гірлянди від українського бренду Jingle Decor. Швидка доставка Новою Поштою 1–2 дні, оплата при отриманні.";

        var faqEntities = GetStoreFaqItems()
            .Select(item => new Dictionary<string, object>
            {
                ["@type"] = "Question",
                ["name"] = item.Question,
                ["acceptedAnswer"] = new Dictionary<string, object>
                {
                    ["@type"] = "Answer",
                    ["text"] = item.Answer
                }
            })
            .ToList();

        var graph = new object[]
        {
            BuildOnlineStoreNode(siteUrl),
            new Dictionary<string, object>
            {
                ["@type"] = "WebSite",
                ["@id"] = $"{siteUrl}/#website",
                ["url"] = siteUrl,
                ["name"] = SeoMetaBuilder.BrandName,
                ["inLanguage"] = "uk-UA",
                ["publisher"] = new Dictionary<string, object> { ["@id"] = $"{siteUrl}/#organization" },
                ["potentialAction"] = new Dictionary<string, object>
                {
                    ["@type"] = "SearchAction",
                    ["target"] = $"{siteUrl}/?query={{search_term_string}}",
                    ["query-input"] = "required name=search_term_string"
                }
            },
            new Dictionary<string, object>
            {
                ["@type"] = "FAQPage",
                ["@id"] = $"{siteUrl}/#faq",
                ["mainEntity"] = faqEntities
            }
        };

        return new SeoStructuredDataResponse
        {
            MetaTitle = metaTitle,
            MetaDescription = metaDescription,
            CanonicalUrl = siteUrl,
            JsonLd = new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org",
                ["@graph"] = graph
            }
        };
    }

    public async Task<SeoStructuredDataResponse?> GetCategoryStructuredDataAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var category = await categoryRepository.GetBySlugAsync(slug.Trim(), cancellationToken);
        if (category is null || !category.IsActive)
            return null;

        var siteUrl = ResolveSiteUrl();
        var canonicalUrl = $"{siteUrl}/catalog/{Uri.EscapeDataString(category.Slug)}";
        var metaTitle = SeoMetaBuilder.BuildCategoryMetaTitle(category.Name, category.MetaTitle);
        var metaDescription = SeoMetaBuilder.BuildCategoryMetaDescription(category.Name, category.MetaDescription);

        var productsPage = await productRepository.GetAllAsync(
            new ProductQueryParameters
            {
                CategoryId = category.Id,
                OnlyActive = true,
                PageNumber = 1,
                PageSize = 24
            },
            cancellationToken);

        var itemListElements = productsPage.Items
            .Select((product, index) => new Dictionary<string, object>
            {
                ["@type"] = "ListItem",
                ["position"] = index + 1,
                ["url"] = $"{siteUrl}/product/{Uri.EscapeDataString(product.Slug)}",
                ["name"] = product.Name
            })
            .ToList();

        var graph = new object[]
        {
            new Dictionary<string, object>
            {
                ["@type"] = "BreadcrumbList",
                ["itemListElement"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["@type"] = "ListItem",
                        ["position"] = 1,
                        ["name"] = "Головна",
                        ["item"] = siteUrl
                    },
                    new Dictionary<string, object>
                    {
                        ["@type"] = "ListItem",
                        ["position"] = 2,
                        ["name"] = category.Name,
                        ["item"] = canonicalUrl
                    }
                }
            },
            new Dictionary<string, object>
            {
                ["@type"] = "CollectionPage",
                ["@id"] = canonicalUrl,
                ["url"] = canonicalUrl,
                ["name"] = category.Name,
                ["description"] = metaDescription,
                ["inLanguage"] = "uk-UA",
                ["isPartOf"] = new Dictionary<string, object> { ["@id"] = $"{siteUrl}/#website" },
                ["mainEntity"] = new Dictionary<string, object>
                {
                    ["@type"] = "ItemList",
                    ["numberOfItems"] = productsPage.TotalCount,
                    ["itemListElement"] = itemListElements
                }
            }
        };

        return new SeoStructuredDataResponse
        {
            MetaTitle = metaTitle,
            MetaDescription = metaDescription,
            CanonicalUrl = canonicalUrl,
            JsonLd = new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org",
                ["@graph"] = graph
            }
        };
    }

    public async Task<SeoStructuredDataResponse?> GetProductStructuredDataAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var product = await productRepository.GetBySlugAsync(slug.Trim(), cancellationToken);
        if (product is null || !product.IsActive)
            return null;

        var siteUrl = ResolveSiteUrl();
        var canonicalUrl = $"{siteUrl}/product/{Uri.EscapeDataString(product.Slug)}";
        var metaTitle = SeoMetaBuilder.BuildProductMetaTitle(product.Name, product.MetaTitle);
        var metaDescription = SeoMetaBuilder.BuildProductMetaDescription(
            product.Name,
            product.Description,
            product.Price,
            product.MetaDescription);
        var cleanDescription = SeoMetaBuilder.StripHtmlAndNormalize(product.Description);
        if (string.IsNullOrEmpty(cleanDescription))
            cleanDescription = metaDescription;

        var imageUrls = new List<string>();
        if (!string.IsNullOrWhiteSpace(product.MainImageUrl))
            imageUrls.Add(ToAbsoluteUrl(product.MainImageUrl, siteUrl));
        foreach (var img in product.Images.OrderBy(i => i.SortOrder))
        {
            var abs = ToAbsoluteUrl(img.Url, siteUrl);
            if (!string.IsNullOrEmpty(abs) && !imageUrls.Contains(abs, StringComparer.OrdinalIgnoreCase))
                imageUrls.Add(abs);
        }

        var offerNode = new Dictionary<string, object>
        {
            ["@type"] = "Offer",
            ["url"] = canonicalUrl,
            ["priceCurrency"] = "UAH",
            ["price"] = product.Price.ToString("0.00", CultureInfo.InvariantCulture),
            ["priceValidUntil"] = DateTime.UtcNow.AddMonths(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["itemCondition"] = "https://schema.org/NewCondition",
            ["availability"] = product.Stock > 0
                ? "https://schema.org/InStock"
                : "https://schema.org/OutOfStock",
            ["seller"] = new Dictionary<string, object>
            {
                ["@type"] = "Organization",
                ["name"] = SeoMetaBuilder.BrandName,
                ["url"] = siteUrl
            },
            ["shippingDetails"] = new Dictionary<string, object>
            {
                ["@type"] = "OfferShippingDetails",
                ["shippingDestination"] = new Dictionary<string, object>
                {
                    ["@type"] = "DefinedRegion",
                    ["addressCountry"] = "UA"
                },
                ["deliveryTime"] = new Dictionary<string, object>
                {
                    ["@type"] = "ShippingDeliveryTime",
                    ["handlingTime"] = new Dictionary<string, object>
                    {
                        ["@type"] = "QuantitativeValue",
                        ["minValue"] = 0,
                        ["maxValue"] = 1,
                        ["unitCode"] = "DAY"
                    },
                    ["transitTime"] = new Dictionary<string, object>
                    {
                        ["@type"] = "QuantitativeValue",
                        ["minValue"] = 1,
                        ["maxValue"] = 3,
                        ["unitCode"] = "DAY"
                    }
                }
            },
            ["hasMerchantReturnPolicy"] = new Dictionary<string, object>
            {
                ["@type"] = "MerchantReturnPolicy",
                ["applicableCountry"] = "UA",
                ["returnPolicyCategory"] = "https://schema.org/MerchantReturnFiniteReturnWindow",
                ["merchantReturnDays"] = 14,
                ["returnMethod"] = "https://schema.org/ReturnByMail"
            }
        };

        var productNode = new Dictionary<string, object>
        {
            ["@type"] = "Product",
            ["@id"] = $"{canonicalUrl}#product",
            ["name"] = product.Name,
            ["sku"] = product.Sku,
            ["description"] = cleanDescription,
            ["url"] = canonicalUrl,
            ["image"] = imageUrls,
            ["brand"] = new Dictionary<string, object>
            {
                ["@type"] = "Brand",
                ["name"] = SeoMetaBuilder.BrandName
            },
            ["offers"] = offerNode
        };

        if (product.Category is not null)
            productNode["category"] = product.Category.Name;

        var approvedReviews = product.Reviews?
            .Where(r => r.Status == ReviewStatus.Approved)
            .OrderByDescending(r => r.CreatedAt)
            .ToList() ?? [];

        if (approvedReviews.Count > 0)
        {
            var avg = decimal.Round((decimal)approvedReviews.Average(r => r.Rating), 1, MidpointRounding.AwayFromZero);
            productNode["aggregateRating"] = new Dictionary<string, object>
            {
                ["@type"] = "AggregateRating",
                ["ratingValue"] = avg.ToString("0.0", CultureInfo.InvariantCulture),
                ["reviewCount"] = approvedReviews.Count,
                ["bestRating"] = 5,
                ["worstRating"] = 1
            };

            productNode["review"] = approvedReviews
                .Take(10)
                .Select(r => new Dictionary<string, object>
                {
                    ["@type"] = "Review",
                    ["author"] = new Dictionary<string, object>
                    {
                        ["@type"] = "Person",
                        ["name"] = r.AuthorName
                    },
                    ["datePublished"] = r.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["reviewBody"] = r.Comment ?? string.Empty,
                    ["reviewRating"] = new Dictionary<string, object>
                    {
                        ["@type"] = "Rating",
                        ["ratingValue"] = r.Rating,
                        ["bestRating"] = 5,
                        ["worstRating"] = 1
                    }
                })
                .ToList();
        }

        var breadcrumbs = new List<object>
        {
            new Dictionary<string, object>
            {
                ["@type"] = "ListItem",
                ["position"] = 1,
                ["name"] = "Головна",
                ["item"] = siteUrl
            }
        };

        if (product.Category is not null && !string.IsNullOrWhiteSpace(product.Category.Slug))
        {
            breadcrumbs.Add(new Dictionary<string, object>
            {
                ["@type"] = "ListItem",
                ["position"] = 2,
                ["name"] = product.Category.Name,
                ["item"] = $"{siteUrl}/catalog/{Uri.EscapeDataString(product.Category.Slug)}"
            });
        }

        breadcrumbs.Add(new Dictionary<string, object>
        {
            ["@type"] = "ListItem",
            ["position"] = breadcrumbs.Count + 1,
            ["name"] = product.Name,
            ["item"] = canonicalUrl
        });

        return new SeoStructuredDataResponse
        {
            MetaTitle = metaTitle,
            MetaDescription = metaDescription,
            CanonicalUrl = canonicalUrl,
            JsonLd = new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org",
                ["@graph"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["@type"] = "BreadcrumbList",
                        ["itemListElement"] = breadcrumbs
                    },
                    productNode
                }
            }
        };
    }

    private async Task<List<Product>> LoadAllActiveProductsAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var firstPage = await productRepository.GetAllAsync(
            new ProductQueryParameters { OnlyActive = true, PageNumber = 1, PageSize = pageSize },
            cancellationToken);

        var all = new List<Product>(Math.Max(firstPage.TotalCount, firstPage.Items.Count()));
        all.AddRange(firstPage.Items);

        for (var page = 2; page <= firstPage.TotalPages; page++)
        {
            var nextPage = await productRepository.GetAllAsync(
                new ProductQueryParameters { OnlyActive = true, PageNumber = page, PageSize = pageSize },
                cancellationToken);
            all.AddRange(nextPage.Items);
        }

        return all;
    }

    private string ResolveSiteUrl()
    {
        var configured = configuration["Seo:PublicSiteUrl"]
            ?? configuration["Referral:PublicSiteUrl"]
            ?? "https://www.jingledecor.com.ua";

        return configured.Trim().TrimEnd('/');
    }

    private static string ToAbsoluteUrl(string url, string siteUrl)
    {
        var trimmed = url.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp))
        {
            return absolute.ToString();
        }

        return $"{siteUrl}/{trimmed.TrimStart('/')}";
    }

    private static string FormatPriceUah(decimal price) =>
        $"{decimal.Round(price, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)} грн";

    private static string FormatAttributesInline(string? attributesJson)
    {
        if (string.IsNullOrWhiteSpace(attributesJson))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(attributesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return string.Empty;

            var parts = new List<string>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(prop.Name, "is_recommended", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(prop.Name, "show_on_homepage", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rawValue = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetRawText(),
                    JsonValueKind.True => "так",
                    JsonValueKind.False => "ні",
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(rawValue))
                    parts.Add($"{prop.Name}: {rawValue.Trim()}");
            }

            return string.Join(", ", parts);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static Dictionary<string, object> BuildOnlineStoreNode(string siteUrl) => new()
    {
        ["@type"] = "OnlineStore",
        ["@id"] = $"{siteUrl}/#organization",
        ["name"] = SeoMetaBuilder.BrandName,
        ["url"] = siteUrl,
        ["description"] = "Український інтернет-магазин литих штучних ялинок, різдвяних вінків, гірлянд та святкового декору.",
        ["currenciesAccepted"] = "UAH",
        ["paymentAccepted"] = "Cash on Delivery, Credit Card, Apple Pay, Google Pay, Monobank, IBAN",
        ["areaServed"] = new Dictionary<string, object>
        {
            ["@type"] = "Country",
            ["name"] = "UA"
        }
    };

    private static IReadOnlyList<(string Question, string Answer)> GetStoreFaqItems() =>
    [
        (
            "Які ялинки представлені в Jingle Decor?",
            "У каталозі Jingle Decor представлені 100% литі штучні ялинки та сосни (зокрема моделі Віденська, Буковельська, Смерека Карпатська, Тріумф у зеленому, блакитному та засніженому виконанні), різдвяні вінки ручної роботи, хвойні та LED-гірлянди."
        ),
        (
            "Чи безпечні литі ялинки для дітей і домашніх тварин?",
            "Так, гілочки виготовляються з первинного харчового поліетилену без сторонніх хімічних запахів, не обсипаються, є гіпоалергенними та пожежобезпечними."
        ),
        (
            "Як здійснюється доставка по Україні?",
            "Доставка здійснюється Новою Поштою у будь-яке працююче відділення або поштомат по Україні протягом 1–2 робочих днів у міцній фірмовій коробці для подальшого зберігання."
        ),
        (
            "Які способи оплати доступні?",
            "Доступна післяплата при отриманні після огляду у відділенні Нової Пошти, онлайн-оплата карткою через еквайринг Monobank (Visa, Mastercard, Apple Pay, Google Pay) із фіскальним чеком пРРО, а також безготівкова оплата на рахунок ФОП за IBAN."
        ),
        (
            "Чи можливий обмін або повернення товару?",
            "Так, обмін і повернення товару належної якості можливі протягом 14 днів з моменту отримання відповідно до Закону України «Про захист прав споживачів»."
        )
    ];
}
