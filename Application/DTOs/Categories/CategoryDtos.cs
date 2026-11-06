using System.ComponentModel.DataAnnotations;

namespace EcommerceBackend.Application.DTOs.Categories;

public sealed class CategoryDto
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string? ParentName { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string? FilterSchema { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
}

public class CreateCategoryDto
{
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$", ErrorMessage = "Slug must be lowercase letters, digits and dashes.")]
    public string Slug { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public string? FilterSchema { get; set; }

    [StringLength(255)]
    public string? MetaTitle { get; set; }

    public string? MetaDescription { get; set; }
}

public sealed class UpdateCategoryDto : CreateCategoryDto
{
}
