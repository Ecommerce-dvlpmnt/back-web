using System.ComponentModel.DataAnnotations;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.DTOs.Reviews;

public sealed class CreateReviewDto
{
    [Required(ErrorMessage = "Ім'я автора є обов'язковим для заповнення.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Ім'я автора повинно містити від 2 до 80 символів.")]
    public string AuthorName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Оцінка є обов'язковою.")]
    [Range(1, 5, ErrorMessage = "Оцінка товару повинна бути в діапазоні від 1 до 5 зірок.")]
    public int Rating { get; init; }

    [Required(ErrorMessage = "Коментар до відгуку є обов'язковим.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Коментар повинен містити від 3 до 500 символів.")]
    public string Comment { get; init; } = string.Empty;

    [StringLength(250, ErrorMessage = "Поле 'Плюси' не повинно перевищувати 250 символів.")]
    public string? Pros { get; init; }

    [StringLength(250, ErrorMessage = "Поле 'Мінуси' не повинно перевищувати 250 символів.")]
    public string? Cons { get; init; }

    [Required(ErrorMessage = "Ідентифікатор товару є обов'язковим.")]
    public Guid ProductId { get; init; }

    public string? Honeypot { get; init; }
}

public sealed class ReviewDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public string? ProductName { get; init; }
    public Guid? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public string AuthorName { get; init; } = string.Empty;
    public int Rating { get; init; }
    public string Comment { get; init; } = string.Empty;
    public string? Pros { get; init; }
    public string? Cons { get; init; }
    public ReviewStatus Status { get; init; }
    public string? AdminComment { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class ProductReviewsSummaryDto
{
    public Guid ProductId { get; init; }
    public double AverageRating { get; init; }
    public int TotalReviews { get; init; }
    public Dictionary<int, int> RatingDistribution { get; init; } = new();
    public IReadOnlyList<ReviewDto> Reviews { get; init; } = Array.Empty<ReviewDto>();
}

public sealed class UpdateReviewStatusDto
{
    [Required(ErrorMessage = "Статус відгуку є обов'язковим.")]
    public ReviewStatus Status { get; init; }

    [StringLength(1000, ErrorMessage = "Внутрішній коментар адміністратора не повинен перевищувати 1000 символів.")]
    public string? AdminComment { get; init; }
}
