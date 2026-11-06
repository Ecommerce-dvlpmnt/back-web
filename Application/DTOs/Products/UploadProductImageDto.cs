using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace EcommerceBackend.Application.DTOs.Products;

public sealed class UploadProductImageDto
{
    [Required]
    public IFormFile File { get; set; } = null!;

    public bool IsMainImage { get; set; }
}
