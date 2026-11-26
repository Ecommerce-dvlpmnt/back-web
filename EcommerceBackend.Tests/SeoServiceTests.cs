using System.Text.Json;
using EcommerceBackend.Application.DTOs.Admin;
using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class SeoServiceTests
{
    [Fact]
    public void SeoMetaBuilder_PreservesCustomMetaAndBuildsCleanFallbacksWhenMissing()
    {
        Assert.Equal(
            "Кастомний Title Саші",
            SeoMetaBuilder.BuildProductMetaTitle("Лита ялинка 1.8 м", "  Кастомний Title Саші  "));

        var generatedTitle = SeoMetaBuilder.BuildProductMetaTitle("Лита ялинка Віденська 1.8 м", null);
        Assert.Contains("Лита ялинка Віденська 1.8 м", generatedTitle);
        Assert.Contains("Jingle Decor", generatedTitle);
        Assert.True(generatedTitle.Length <= 68);

        var generatedDesc = SeoMetaBuilder.BuildProductMetaDescription(
            "Лита ялинка Віденська 1.8 м",
            "<p>Густа 100% лита хвоя без запаху для вашої оселі.</p> Відеоогляд",
            3490m,
            null);

        Assert.DoesNotContain("<p>", generatedDesc);
        Assert.DoesNotContain("Відеоогляд", generatedDesc);
        Assert.Contains("3490 грн", generatedDesc);
        Assert.Contains("Jingle Decor", generatedDesc);
        Assert.True(generatedDesc.Length <= 160);
    }

    [Fact]
    public async Task ProductService_PublicDtoGeneratesSeoAndApprovedRatings_WhileAdminDtoKeepsRawNullMeta()
    {
        var repo = new InMemoryProductRepo();
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            CategoryId = Guid.NewGuid(),
            SupplierId = Guid.NewGuid(),
            Sku = "TREE-900",
            Name = "Лита ялинка Буковельська 2.1 м",
            Slug = "lyta-yalynka-bukovelska-2-1-m",
            Description = "Преміальна лита хвоя ручної роботи.",
            Price = 4200m,
            DropPrice = 2500m,
            Stock = 7,
            MetaTitle = null,
            MetaDescription = null,
            IsActive = true,
            Images =
            [
                new ProductImage { Id = Guid.NewGuid(), Url = "https://cdn.example.com/1.jpg", Alt = null, SortOrder = 0 },
                new ProductImage { Id = Guid.NewGuid(), Url = "https://cdn.example.com/2.jpg", Alt = "", SortOrder = 1 }
            ],
            Reviews =
            [
                new Review { Id = Guid.NewGuid(), ProductId = productId, Rating = 5, Status = ReviewStatus.Approved, AuthorName = "Олена" },
                new Review { Id = Guid.NewGuid(), ProductId = productId, Rating = 4, Status = ReviewStatus.Approved, AuthorName = "Ігор" },
                new Review { Id = Guid.NewGuid(), ProductId = productId, Rating = 1, Status = ReviewStatus.Pending, AuthorName = "Спам" }
            ]
        };
        repo.Items.Add(product);

        var service = new ProductService(repo, new NoopImageStorage(), new NoopAuditLog());

        var adminDto = await service.GetByIdAsync(productId);
        Assert.NotNull(adminDto);
        Assert.Null(adminDto!.MetaTitle);
        Assert.Null(adminDto.MetaDescription);
        Assert.Equal(2, adminDto.ReviewCount);
        Assert.Equal(4.5m, adminDto.AverageRating);

        var publicDto = await service.GetPublicBySlugAsync(product.Slug);
        Assert.NotNull(publicDto);
        Assert.False(string.IsNullOrWhiteSpace(publicDto!.MetaTitle));
        Assert.False(string.IsNullOrWhiteSpace(publicDto.MetaDescription));
        Assert.Equal(2, publicDto.ReviewCount);
        Assert.Equal(4.5m, publicDto.AverageRating);
        Assert.Equal("Лита ялинка Буковельська 2.1 м", publicDto.Images[0].Alt);
        Assert.Equal("Лита ялинка Буковельська 2.1 м — фото 2", publicDto.Images[1].Alt);
    }

    [Fact]
    public async Task SeoService_GeneratesLlmsFilesAndValidSchemaOrgGraphs()
    {
        var categoryId = Guid.NewGuid();
        var category = new Category
        {
            Id = categoryId,
            Name = "Литі ялинки",
            Slug = "lyti-yalynky",
            IsActive = true,
            SortOrder = 1
        };

        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            CategoryId = categoryId,
            Category = category,
            Sku = "JD-180",
            Name = "Лита ялинка Віденська зелена 1.8 м",
            Slug = "videnska-zelena-180",
            Description = "Натуральний вигляд хвої, металева підставка в комплекті.",
            Attributes = """{"Висота":"1.8 м","Матеріал":"100% литий ПЕ"}""",
            Price = 3650m,
            OldPrice = 4100m,
            Stock = 12,
            MainImageUrl = "https://cdn.example.com/videnska.jpg",
            IsActive = true,
            Reviews =
            [
                new Review
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    AuthorName = "Марія",
                    Rating = 5,
                    Comment = "Дуже пишна ялинка!",
                    Status = ReviewStatus.Approved,
                    CreatedAt = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc)
                }
            ]
        };

        var productRepo = new InMemoryProductRepo();
        productRepo.Items.Add(product);
        var categoryRepo = new InMemoryCategoryRepo([category]);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Referral:PublicSiteUrl"] = "https://www.jingledecor.com.ua"
            })
            .Build();

        var seoService = new SeoService(productRepo, categoryRepo, cache, config);

        var llmsTxt = await seoService.GetLlmsTxtAsync();
        Assert.Contains("# Jingle Decor", llmsTxt);
        Assert.Contains("https://www.jingledecor.com.ua/catalog/lyti-yalynky", llmsTxt);
        Assert.Contains("https://www.jingledecor.com.ua/product/videnska-zelena-180", llmsTxt);
        Assert.Contains("3650 грн", llmsTxt);

        var llmsFull = await seoService.GetLlmsFullTxtAsync();
        Assert.Contains("Часті запитання (FAQ)", llmsFull);
        Assert.Contains("JD-180", llmsFull);
        Assert.Contains("Висота: 1.8 м", llmsFull);

        var homeData = seoService.GetHomeStructuredData();
        var homeJson = JsonSerializer.Serialize(homeData.JsonLd);
        Assert.Contains("OnlineStore", homeJson);
        Assert.Contains("WebSite", homeJson);
        Assert.Contains("FAQPage", homeJson);

        var categoryData = await seoService.GetCategoryStructuredDataAsync("lyti-yalynky");
        Assert.NotNull(categoryData);
        var categoryJson = JsonSerializer.Serialize(categoryData!.JsonLd);
        Assert.Contains("CollectionPage", categoryJson);
        Assert.Contains("BreadcrumbList", categoryJson);

        var productData = await seoService.GetProductStructuredDataAsync("videnska-zelena-180");
        Assert.NotNull(productData);
        var productJson = JsonSerializer.Serialize(productData!.JsonLd);
        Assert.Contains("OfferShippingDetails", productJson);
        Assert.Contains("MerchantReturnPolicy", productJson);
        Assert.Contains("AggregateRating", productJson);
        Assert.Contains("5.0", productJson);
    }

    private sealed class InMemoryProductRepo : IProductRepository
    {
        public List<Product> Items { get; } = [];

        public Task<PagedResult<Product>> GetAllAsync(ProductQueryParameters query, CancellationToken cancellationToken = default)
        {
            var filtered = Items.AsEnumerable();
            if (query.OnlyActive == true)
                filtered = filtered.Where(p => p.IsActive);
            if (query.CategoryId.HasValue)
                filtered = filtered.Where(p => p.CategoryId == query.CategoryId.Value);

            var list = filtered.ToList();
            return Task.FromResult(new PagedResult<Product>
            {
                Items = list,
                TotalCount = list.Count,
                PageNumber = 1,
                PageSize = 100,
                TotalPages = 1
            });
        }

        public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

        public Task<Product?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(p => p.Slug == slug));

        public Task<Product> CreateAsync(Product product, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Product?> UpdateAsync(Product product, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Product?> UpdateImageAsync(Guid productId, string? mainImageUrl, ProductImage? image, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> HasOrdersAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> UpdateStockAsync(Guid id, int stock, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> ExistsBySkuOrSlugAsync(string sku, string slug, Guid? excludeId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class InMemoryCategoryRepo(IReadOnlyList<Category> categories) : ICategoryRepository
    {
        public Task<IReadOnlyList<Category>> GetAllAsync(bool? onlyActive, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Category>>(
                onlyActive == true ? categories.Where(c => c.IsActive).ToList() : categories);

        public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(categories.FirstOrDefault(c => c.Id == id));

        public Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(categories.FirstOrDefault(c => c.Slug == slug));

        public Task<Category> CreateAsync(Category category, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<Category?> UpdateAsync(Category category, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> ExistsBySlugAsync(string slug, Guid? excludeId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> HasChildrenOrProductsAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class NoopImageStorage : IImageStorageService
    {
        public Task<string> UploadImageAsync(IFormFile file, string folderName, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);
    }

    private sealed class NoopAuditLog : IAuditLogService
    {
        public Task LogAsync(string action, string entityType, string entityId, object details, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<PagedResult<AdminAuditLogDto>> GetPagedAsync(AuditLogQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PagedResult<AdminAuditLogDto>());

        public Task<IReadOnlyList<AdminAuditLog>> GetLogsAsync(string? entityType = null, string? entityId = null, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdminAuditLog>>([]);
    }
}
