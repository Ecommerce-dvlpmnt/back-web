using System.ComponentModel.DataAnnotations;

namespace EcommerceBackend.Application.DTOs.Suppliers;

public sealed class SupplierDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

public class CreateSupplierDto
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [StringLength(150)]
    public string? Contact { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}
