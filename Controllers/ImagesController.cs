using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/products/{productId:guid}/images")]
[Produces("application/json")]
public sealed class ImagesController(IProductService productService) : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> UploadProductImage(
        Guid productId,
        [FromForm] UploadProductImageDto request,
        CancellationToken cancellationToken)
    {
        var product = await productService.UploadImageToProductAsync(
            productId,
            request.File,
            request.IsMainImage,
            cancellationToken);

        return Ok(product);
    }
}
