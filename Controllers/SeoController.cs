using EcommerceBackend.Application.DTOs.Seo;
using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
public sealed class SeoController(ISeoService seoService) : ControllerBase
{
    /// <summary>Стислий Markdown-довідник магазину та каталогу за стандартом llmstxt.org для AI-агентів (ChatGPT, Perplexity, Claude, Gemini).</summary>
    [HttpGet("/llms.txt")]
    [HttpGet("api/seo/llms.txt")]
    [Produces("text/plain")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLlmsTxt(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "public, max-age=300, stale-while-revalidate=600";
        var content = await seoService.GetLlmsTxtAsync(cancellationToken);
        return Content(content, "text/plain; charset=utf-8");
    }

    /// <summary>Повна база знань каталогу, характеристик, цін і FAQ у форматі Markdown для генеративних пошукових систем (GEO).</summary>
    [HttpGet("/llms-full.txt")]
    [HttpGet("api/seo/llms-full.txt")]
    [Produces("text/plain")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLlmsFullTxt(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "public, max-age=300, stale-while-revalidate=600";
        var content = await seoService.GetLlmsFullTxtAsync(cancellationToken);
        return Content(content, "text/plain; charset=utf-8");
    }

    /// <summary>Готова Schema.org JSON-LD розмітка (OnlineStore, WebSite, FAQPage) та SEO-метадані для головної сторінки.</summary>
    [HttpGet("api/seo/structured-data/home")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(SeoStructuredDataResponse), StatusCodes.Status200OK)]
    public ActionResult<SeoStructuredDataResponse> GetHomeStructuredData()
    {
        Response.Headers.CacheControl = "public, max-age=300, stale-while-revalidate=600";
        return Ok(seoService.GetHomeStructuredData());
    }

    /// <summary>Готова Schema.org JSON-LD розмітка (CollectionPage, ItemList, BreadcrumbList) та SEO-метадані для категорії.</summary>
    [HttpGet("api/seo/structured-data/category/{slug}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(SeoStructuredDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SeoStructuredDataResponse>> GetCategoryStructuredData(
        string slug,
        CancellationToken cancellationToken)
    {
        var data = await seoService.GetCategoryStructuredDataAsync(slug, cancellationToken);
        if (data is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=300, stale-while-revalidate=600";
        return Ok(data);
    }

    /// <summary>Готова Schema.org JSON-LD розмітка (Product, Offer, Shipping, ReturnPolicy, AggregateRating, BreadcrumbList) для картки товару.</summary>
    [HttpGet("api/seo/structured-data/product/{slug}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(SeoStructuredDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SeoStructuredDataResponse>> GetProductStructuredData(
        string slug,
        CancellationToken cancellationToken)
    {
        var data = await seoService.GetProductStructuredDataAsync(slug, cancellationToken);
        if (data is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=300, stale-while-revalidate=600";
        return Ok(data);
    }
}
