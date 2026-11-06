using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/products/{productId:guid}/images")]
[Produces("application/json")]
public sealed class ImagesController(IProductService productService) : ControllerBase
{
    /// <summary>Uploads a main product image or adds an image to its gallery.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ProductDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> UploadProductImage(
        Guid productId,
        [FromForm] UploadProductImageDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            var product = await productService.UploadImageToProductAsync(
                productId,
                request.File,
                request.IsMainImage,
                cancellationToken);

            return Ok(product);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid image",
                Detail = exception.Message
            });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Product not found",
                Detail = exception.Message
            });
        }
    }
}
