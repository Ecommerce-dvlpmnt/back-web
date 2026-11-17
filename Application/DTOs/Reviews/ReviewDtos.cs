using System.ComponentModel.DataAnnotations;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.DTOs.Reviews;

public sealed class CreateReviewDto
{
    [Required(ErrorMessage = "Имя автора обязательно для заполнения.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "Имя автора должно содержать от 2 до 80 символов.")]
    public string AuthorName { get; init; } = string.Empty;

    [Required(ErrorMessage = "Оценка обязательна.")]
    [Range(1, 5, ErrorMessage = "Оценка товара должна быть в диапазоне от 1 до 5 звезд.")]
    public int Rating { get; init; }

    [Required(ErrorMessage = "Комментарий к отзыву обязателен.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "Комментарий должен содержать от 3 до 500 символов.")]
    public string Comment { get; init; } = string.Empty;

    [StringLength(250, ErrorMessage = "Поле 'Плюсы' не должно превышать 250 символов.")]
    public string? Pros { get; init; }

    [StringLength(250, ErrorMessage = "Поле 'Минусы' не должно превышать 250 символов.")]
    public string? Cons { get; init; }

    [Required(ErrorMessage = "Идентификатор товара обязателен.")]
    public Guid ProductId { get; init; }

    /// <summary>
    /// Невидимое поле-ловушка для спам-ботов (Honeypot). На фронтенде скрыто через CSS (display:none).
    /// Реальные пользователи оставляют его пустым. Если бот заполнил — запрос отклоняется как спам.
    /// </summary>
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
    [Required(ErrorMessage = "Статус отзыва обязателен.")]
    public ReviewStatus Status { get; init; }

    [StringLength(1000, ErrorMessage = "Внутренний комментарий администратора не должен превышать 1000 символов.")]
    public string? AdminComment { get; init; }
}
