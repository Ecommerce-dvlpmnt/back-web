using EcommerceBackend.Application.DTOs.Admin;
using EcommerceBackend.Application.DTOs.Reviews;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class ReviewServiceTests
{
    [Fact]
    public async Task CreateReview_Success_CreatesWithPendingStatusAndResolvedCategoryId()
    {
        var categoryId = Guid.NewGuid();
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Новогодняя гирлянда",
            CategoryId = categoryId,
            Sku = "SKU-1",
            Slug = "girlyanda",
            Price = 500m
        };

        var productRepo = new FakeProductRepository(product);
        var reviewRepo = new FakeReviewRepository();
        var service = new ReviewService(reviewRepo, productRepo, new FakeAuditLogService());

        var request = new CreateReviewDto
        {
            ProductId = product.Id,
            AuthorName = "Иван Петров",
            Rating = 5,
            Comment = "Отличная гирлянда, светит очень ярко!",
            Pros = "Яркие диоды, длинный провод",
            Cons = "Нет пульта ДУ",
            Honeypot = null // Честный пользователь оставляет пустым
        };

        var result = await service.CreateReviewAsync(request);

        Assert.NotNull(result);
        Assert.Equal("Иван Петров", result.AuthorName);
        Assert.Equal(5, result.Rating);
        Assert.Equal("Отличная гирлянда, светит очень ярко!", result.Comment);
        Assert.Equal("Яркие диоды, длинный провод", result.Pros);
        Assert.Equal("Нет пульта ДУ", result.Cons);
        Assert.Equal(ReviewStatus.Pending, result.Status); // Должен быть Pending до модерации
        Assert.Equal(categoryId, result.CategoryId); // Автоматически привязана категория товара

        Assert.NotNull(reviewRepo.Created);
        Assert.Equal(ReviewStatus.Pending, reviewRepo.Created!.Status);
    }

    [Fact]
    public async Task CreateReview_HoneypotFilled_RejectsAsSpam()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Тестовый товар",
            Sku = "SKU-2",
            Slug = "test",
            Price = 100m
        };

        var productRepo = new FakeProductRepository(product);
        var reviewRepo = new FakeReviewRepository();
        var service = new ReviewService(reviewRepo, productRepo, new FakeAuditLogService());

        var botRequest = new CreateReviewDto
        {
            ProductId = product.Id,
            AuthorName = "Bot 123",
            Rating = 5,
            Comment = "Buy cheap crypto now!",
            Honeypot = "http://spam-link.com" // Бот заполнил скрытое поле
        };

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateReviewAsync(botRequest));

        Assert.Contains("Виявлено спам-активність", exception.Message);
        Assert.Null(reviewRepo.Created);
    }

    [Fact]
    public async Task CreateReview_ProductNotFound_ThrowsKeyNotFoundException()
    {
        var productRepo = new FakeProductRepository(); // Нет товаров
        var reviewRepo = new FakeReviewRepository();
        var service = new ReviewService(reviewRepo, productRepo, new FakeAuditLogService());

        var request = new CreateReviewDto
        {
            ProductId = Guid.NewGuid(),
            AuthorName = "Анна",
            Rating = 4,
            Comment = "Хороший товар"
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CreateReviewAsync(request));
    }

    [Fact]
    public async Task GetProductReviews_ReturnsOnlyApprovedWithCorrectAggregates()
    {
        var productId = Guid.NewGuid();
        var reviewRepo = new FakeReviewRepository();

        // 3 одобренных отзыва: оценки 5, 5, 4 (среднее: 4.7)
        reviewRepo.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AuthorName = "User 1",
            Rating = 5,
            Comment = "Супер!",
            Status = ReviewStatus.Approved,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });
        reviewRepo.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AuthorName = "User 2",
            Rating = 5,
            Comment = "Класс!",
            Status = ReviewStatus.Approved,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        reviewRepo.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AuthorName = "User 3",
            Rating = 4,
            Comment = "Нормально",
            Status = ReviewStatus.Approved,
            CreatedAt = DateTime.UtcNow
        });

        // 1 неодобренный отзыв (Pending) - не должен попасть в выборку витрины
        reviewRepo.Reviews.Add(new Review
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            AuthorName = "Spam User",
            Rating = 1,
            Comment = "Не спамьте",
            Status = ReviewStatus.Pending
        });

        var productRepo = new FakeProductRepository();
        var service = new ReviewService(reviewRepo, productRepo, new FakeAuditLogService());

        var summary = await service.GetProductReviewsAsync(productId);

        Assert.Equal(3, summary.TotalReviews);
        Assert.Equal(4.7, summary.AverageRating);
        Assert.Equal(2, summary.RatingDistribution[5]);
        Assert.Equal(1, summary.RatingDistribution[4]);
        Assert.Equal(0, summary.RatingDistribution[3]);
        Assert.Equal(0, summary.RatingDistribution[2]);
        Assert.Equal(0, summary.RatingDistribution[1]);
        Assert.Equal(3, summary.Reviews.Count);
    }

    [Fact]
    public async Task UpdateStatus_ApprovesReviewAndAddsAdminComment()
    {
        var review = new Review
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            AuthorName = "Покупатель",
            Rating = 5,
            Comment = "Все пришло вовремя",
            Status = ReviewStatus.Pending
        };

        var reviewRepo = new FakeReviewRepository();
        reviewRepo.Reviews.Add(review);
        var productRepo = new FakeProductRepository();
        var auditLogService = new FakeAuditLogService();
        var service = new ReviewService(reviewRepo, productRepo, auditLogService);

        var result = await service.UpdateStatusAsync(review.Id, new UpdateReviewStatusDto
        {
            Status = ReviewStatus.Approved,
            AdminComment = "Проверено по номеру ТТН"
        });

        Assert.NotNull(result);
        Assert.Equal(ReviewStatus.Approved, result!.Status);
        Assert.Equal("Проверено по номеру ТТН", result.AdminComment);
        Assert.Single(auditLogService.Logs);
        Assert.Equal(AuditLogActions.ReviewStatusChanged, auditLogService.Logs[0].Action);
        Assert.Equal(AuditLogEntityTypes.Review, auditLogService.Logs[0].EntityType);
    }

    [Fact]
    public async Task Delete_RemovesReview()
    {
        var review = new Review
        {
            Id = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            AuthorName = "Спамер",
            Rating = 1,
            Comment = "Спам",
            Status = ReviewStatus.Rejected
        };

        var reviewRepo = new FakeReviewRepository();
        reviewRepo.Reviews.Add(review);
        var productRepo = new FakeProductRepository();
        var service = new ReviewService(reviewRepo, productRepo, new FakeAuditLogService());

        var deleted = await service.DeleteAsync(review.Id);

        Assert.True(deleted);
        Assert.Empty(reviewRepo.Reviews);
    }

    private sealed class FakeAuditLogService : IAuditLogService
    {
        public List<AdminAuditLog> Logs { get; } = new();

        public Task LogAsync(
            string action,
            string entityType,
            string entityId,
            object details,
            CancellationToken cancellationToken = default)
        {
            Logs.Add(new AdminAuditLog
            {
                Id = Guid.NewGuid(),
                AdminUsername = "test",
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Details = System.Text.Json.JsonSerializer.Serialize(details),
                CreatedAt = DateTime.UtcNow
            });

            return Task.CompletedTask;
        }

        public Task<EcommerceBackend.Application.DTOs.Products.PagedResult<AdminAuditLogDto>> GetPagedAsync(
            AuditLogQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EcommerceBackend.Application.DTOs.Products.PagedResult<AdminAuditLogDto>());
    }

    private sealed class FakeReviewRepository : IReviewRepository
    {
        public List<Review> Reviews { get; } = new();
        public Review? Created { get; private set; }

        public Task<Review> CreateAsync(Review review, CancellationToken cancellationToken = default)
        {
            Created = review;
            Reviews.Add(review);
            return Task.FromResult(review);
        }

        public Task<Review?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Reviews.FirstOrDefault(r => r.Id == id));

        public Task<IReadOnlyList<Review>> GetApprovedByProductIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            var list = Reviews
                .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved)
                .OrderByDescending(r => r.CreatedAt)
                .ToList();
            return Task.FromResult<IReadOnlyList<Review>>(list);
        }

        public Task<IReadOnlyList<Review>> GetAdminReviewsAsync(
            ReviewStatus? status,
            Guid? productId,
            Guid? categoryId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var query = Reviews.AsEnumerable();
            if (status.HasValue) query = query.Where(r => r.Status == status.Value);
            if (productId.HasValue) query = query.Where(r => r.ProductId == productId.Value);
            if (categoryId.HasValue) query = query.Where(r => r.CategoryId == categoryId.Value);

            var list = query.OrderByDescending(r => r.CreatedAt).Skip(skip).Take(take).ToList();
            return Task.FromResult<IReadOnlyList<Review>>(list);
        }

        public Task<int> GetAdminReviewsCountAsync(
            ReviewStatus? status,
            Guid? productId,
            Guid? categoryId,
            CancellationToken cancellationToken = default)
        {
            var query = Reviews.AsEnumerable();
            if (status.HasValue) query = query.Where(r => r.Status == status.Value);
            if (productId.HasValue) query = query.Where(r => r.ProductId == productId.Value);
            if (categoryId.HasValue) query = query.Where(r => r.CategoryId == categoryId.Value);

            return Task.FromResult(query.Count());
        }

        public Task<Review?> UpdateAsync(Review review, CancellationToken cancellationToken = default)
        {
            var idx = Reviews.FindIndex(r => r.Id == review.Id);
            if (idx == -1) return Task.FromResult<Review?>(null);
            Reviews[idx] = review;
            return Task.FromResult<Review?>(review);
        }

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var removed = Reviews.RemoveAll(r => r.Id == id) > 0;
            return Task.FromResult(removed);
        }
    }

    private sealed class FakeProductRepository : IProductRepository
    {
        private readonly Product? _product;

        public FakeProductRepository(Product? product = null)
        {
            _product = product;
        }

        public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_product?.Id == id ? _product : null);

        public Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(_product?.Slug == slug ? _product : null);

        public Task<EcommerceBackend.Application.DTOs.Products.PagedResult<Product>> GetAllAsync(
            EcommerceBackend.Application.DTOs.Products.ProductQueryParameters query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EcommerceBackend.Application.DTOs.Products.PagedResult<Product>
            {
                Items = _product is not null ? new[] { _product } : Array.Empty<Product>(),
                TotalCount = _product is not null ? 1 : 0,
                PageNumber = 1,
                PageSize = 20
            });

        public Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default) =>
            Task.FromResult(product);

        public Task<Product?> UpdateAsync(Product product, CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(product);

        public Task<bool> UpdateStockAsync(Guid id, int stock, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> ExistsBySkuOrSlugAsync(string sku, string slug, Guid? excludeId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<Product?> UpdateImageAsync(Guid id, string? mainImageUrl, ProductImage? image, CancellationToken cancellationToken = default) =>
            Task.FromResult(_product);
    }
}
