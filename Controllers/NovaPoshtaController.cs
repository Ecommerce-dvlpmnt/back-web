using EcommerceBackend.Application.DTOs.NovaPoshta;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

/// <summary>Довідники Нової Пошти для вибору відділення під час оформлення замовлення.</summary>
[ApiController]
[Route("api/novaposhta")]
[Produces("application/json")]
public sealed class NovaPoshtaController(INovaPoshtaClient novaPoshtaClient) : ControllerBase
{
    /// <summary>Пошук міст Нової Пошти за назвою.</summary>
    [HttpGet("cities")]
    [ProducesResponseType(typeof(NovaPoshtaResult<NovaPoshtaCity>), StatusCodes.Status200OK)]
    public async Task<ActionResult<NovaPoshtaResult<NovaPoshtaCity>>> GetCities(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return Ok(new NovaPoshtaResult<NovaPoshtaCity> { Success = true });
        }

        var result = await novaPoshtaClient.GetCitiesAsync(search, cancellationToken);
        return Ok(result);
    }

    /// <summary>Відділення та поштомати Нової Пошти у вибраному місті.</summary>
    [HttpGet("warehouses")]
    [ProducesResponseType(typeof(NovaPoshtaResult<NovaPoshtaWarehouse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NovaPoshtaResult<NovaPoshtaWarehouse>>> GetWarehouses(
        [FromQuery] string? cityRef,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cityRef))
        {
            throw new ArgumentException("Параметр cityRef є обов'язковим.", nameof(cityRef));
        }

        var result = await novaPoshtaClient.GetWarehousesAsync(cityRef, cancellationToken);
        return Ok(result);
    }
}
