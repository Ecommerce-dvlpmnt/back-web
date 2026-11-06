using System.ComponentModel.DataAnnotations;

namespace EcommerceBackend.Application.DTOs.Suppliers;

/// <summary>Supplier details returned by the API.</summary>
public sealed class SupplierDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Fields required to create a supplier.</summary>
public class CreateSupplierDto
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Phone number, Telegram handle or contact person.</summary>
    [StringLength(150)]
    public string? Contact { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    /// <summary>Internal notes: payment terms, returns policy, stock confirmation.</summary>
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;
}
