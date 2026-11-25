using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EcommerceBackend.Application.Security;
using EcommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        await SeedAdminsAsync(context, cancellationToken);
        await SeedSuppliersAsync(context, cancellationToken);
        var categoryMap = await SeedCategoriesAsync(context, cancellationToken);
        await SeedProductsAsync(context, categoryMap, cancellationToken);
    }

    private static async Task SeedAdminsAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var usernamesToSeed = new (string Username, string Name, string Role, string PartnerRole, decimal FixedSharePercent)[]
        {
            ("gryftd", "Даниил (gryftd)", "Admin", "Lead / Dev", 35.0m),
            ("Nesh10n", "Саша (Nesh10n)", "Admin", "Dev / Backend", 35.0m),
            ("var404", "Максим (var404)", "Admin", "Marketing", 20.0m),
            ("gdm1234", "Партнер 4 (gdm1234)", "Admin", "Operations", 10.0m)
        };

        var hasher = new PasswordHasher();
        var defaultPasswordHash = hasher.HashPassword("Admin12345!");
        var now = DateTime.UtcNow;

        foreach (var (username, name, role, partnerRole, fixedSharePercent) in usernamesToSeed)
        {
            var admin = await context.Admins.FirstOrDefaultAsync(a => a.Username.ToLower() == username.ToLower(), cancellationToken);
            if (admin is null)
            {
                context.Admins.Add(new Admin
                {
                    Id = Guid.NewGuid(),
                    Username = username,
                    Name = name,
                    Email = $"{username.ToLower()}@ecommerce.local",
                    PasswordHash = defaultPasswordHash,
                    Role = role,
                    PartnerRole = partnerRole,
                    FixedSharePercent = fixedSharePercent,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                if (string.IsNullOrEmpty(admin.PartnerRole))
                {
                    admin.PartnerRole = partnerRole;
                    admin.FixedSharePercent = fixedSharePercent;
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedSuppliersAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (!await context.Suppliers.AnyAsync(s => s.Name.Contains("Ялинка Року"), cancellationToken))
        {
            context.Suppliers.Add(new Supplier
            {
                Id = Guid.NewGuid(),
                Name = "Ялинка Року (Фабрика)",
                Contact = "+38 (098) 765-43-21",
                City = "Івано-Франківськ",
                Notes = "Прямий український виробник штучних литих ялинок та новорічного декору",
                IsActive = true
            });
            await context.SaveChangesAsync(cancellationToken);
        }

        if (!await context.Suppliers.AnyAsync(s => s.Name == "Ялинка-Трейд" || s.Name == "Ёлочка-Трейд", cancellationToken))
        {
            context.Suppliers.Add(new Supplier
            {
                Id = Guid.NewGuid(),
                Name = "Ялинка-Трейд",
                Contact = "+380501234567",
                City = "Харків",
                Notes = "Постачальник гірлянд, іграшок та декору",
                IsActive = true
            });
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<Dictionary<string, Guid>> SeedCategoriesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var treeCategories = new (string Name, string Slug, string Title, string Desc)[]
        {
            ("Литі ялинки", "lyti-yalynky", "Литі штучні ялинки від виробника — Ялинка Року", "Купити литу штучну ялинку з доставкою по Україні. Смерека, Віденська, Буковельська."),
            ("Преміум ялинки", "premium-yalynky", "Преміум штучні ялинки на крабах — Нью-Йорк, Тріумф, Royal", "Саморозкладні литі ялинки на шарнірах (крабах) преміум якості."),
            ("Засніжені ялинки", "zasnizheni-yalynky", "Засніжені штучні ялинки — Альпійська та Смерека", "Штучні ялинки з ефектом натурального засніження та паморозі."),
            ("Ялинки та туї в горщиках", "v-horshchykakh", "Штучні ялинки та туї в горщиках — для дому та офісу", "Компактні настільні ялинки в лозі та інтер'єрні вічнозелені туї."),
            ("Ялинки ПВХ", "yalynky-pvkh", "Класичні штучні ялинки ПВХ — Казка, Елітна, Королева", "Бюджетні та пишні штучні ялинки з шишками та калиною."),
            ("Різдвяні вінки", "rizdvyani-vinky", "Різдвяні вінки з литої хвої та декором — Ялинка Року", "Купити новорічний вінок на двері з підсвіткою та святковим декором."),
            ("Хвойні гірлянди", "khvoyni-hirlyandy", "Штучні хвойні гірлянди з литої хвої — Ялинка Року", "Новорічні гірлянди з шишками, калиною та засніженням для перил, камінів та вікон.")
        };

        var categoryMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < treeCategories.Length; i++)
        {
            var (name, slug, title, desc) = treeCategories[i];
            var existing = await context.Categories.FirstOrDefaultAsync(c => c.Slug == slug, cancellationToken);
            if (existing is null)
            {
                var newCat = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    Slug = slug,
                    ParentId = null,
                    SortOrder = i + 1,
                    IsActive = true,
                    FilterSchema = "{}",
                    MetaTitle = title,
                    MetaDescription = desc
                };
                context.Categories.Add(newCat);
                categoryMap[slug] = newCat.Id;
            }
            else
            {
                existing.IsActive = true;
                categoryMap[slug] = existing.Id;
            }
        }

        // Deactivate old dummy categories without deleting them to avoid FK issues
        var oldDummySlugs = new[] { "christmas-ornaments", "garlands", "cozy-textile", "gifts" };
        var oldCategories = await context.Categories
            .Where(c => oldDummySlugs.Contains(c.Slug))
            .ToListAsync(cancellationToken);
        foreach (var oldCat in oldCategories)
        {
            oldCat.IsActive = false;
        }

        await context.SaveChangesAsync(cancellationToken);
        return categoryMap;
    }

    private static async Task SeedProductsAsync(
        AppDbContext context,
        Dictionary<string, Guid> categoryMap,
        CancellationToken cancellationToken)
    {
        // Deactivate old dummy products (stock socks, mugs, test items)
        var dummyProducts = await context.Products
            .Where(p => p.Sku.StartsWith("NY-") || (p.MainImageUrl != null && p.MainImageUrl.Contains("picsum.photos")))
            .ToListAsync(cancellationToken);

        foreach (var dp in dummyProducts)
        {
            dp.IsActive = false;
        }
        await context.SaveChangesAsync(cancellationToken);

        // Fetch existing JD- or YR- products
        var existingYrProducts = await context.Products
            .Where(p => p.Sku.StartsWith("JD-") || p.Sku.StartsWith("YR-"))
            .ToListAsync(cancellationToken);

        // If any existing product has a null image, clean it up
        if (existingYrProducts.Count > 0 && existingYrProducts.Any(p => string.IsNullOrEmpty(p.MainImageUrl)))
        {
            var yrIds = existingYrProducts.Select(p => p.Id).ToList();
            var yrImages = await context.ProductImages.Where(img => yrIds.Contains(img.ProductId)).ToListAsync(cancellationToken);
            context.ProductImages.RemoveRange(yrImages);
            context.Products.RemoveRange(existingYrProducts);
            await context.SaveChangesAsync(cancellationToken);
            existingYrProducts.Clear();
        }

        var existingYrSkus = existingYrProducts.Select(p => p.Sku).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingYrSlugs = existingYrProducts.Select(p => p.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var supplier = await context.Suppliers.FirstOrDefaultAsync(s => s.Name.Contains("Ялинка Року"), cancellationToken)
                       ?? await context.Suppliers.FirstAsync(cancellationToken);

        var possiblePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "yalynka_parsed_25.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Data", "yalynka_parsed_25.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "yalynka_parsed_25.json")
        };

        var jsonPath = possiblePaths.FirstOrDefault(File.Exists);
        if (jsonPath == null)
            return;

        var jsonContent = await File.ReadAllTextAsync(jsonPath, cancellationToken);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var parsedItems = JsonSerializer.Deserialize<List<YalynkaItemDto>>(jsonContent, jsonOptions);

        if (parsedItems == null || parsedItems.Count == 0)
            return;

        var defaultCategoryId = categoryMap.Values.First();
        var now = DateTime.UtcNow;

        for (var i = 0; i < parsedItems.Count; i++)
        {
            var item = parsedItems[i];
            var sku = $"JD-{(i + 1):D4}";

            if (existingYrSkus.Contains(sku) || existingYrSlugs.Contains(item.Slug))
                continue;

            var categoryId = categoryMap.TryGetValue(item.CategorySlug, out var catId) ? catId : defaultCategoryId;
            var productId = Guid.NewGuid();

            decimal weight = 3.0m;
            foreach (var (k, v) in item.Attributes)
            {
                if (k.Contains("вага", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(v, @"(\d+([.,]\d+)?)");
                    if (match.Success && decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsedWeight))
                    {
                        weight = parsedWeight;
                    }
                }
            }

            var product = new Product
            {
                Id = productId,
                CategoryId = categoryId,
                SupplierId = supplier.Id,
                Sku = sku,
                Name = item.Name,
                Slug = item.Slug,
                Description = item.Description,
                DropPrice = item.DropPrice,
                Price = item.Price,
                OldPrice = item.OldPrice,
                Stock = 20 + (i % 15),
                StockUpdatedAt = now,
                MainImageUrl = item.MainImage,
                Attributes = JsonSerializer.Serialize(item.Attributes),
                MetaTitle = $"{item.Name} — купити в інтернет-магазині «Ялинка Року»",
                MetaDescription = $"{item.Name} від українського виробника «Ялинка Року». Швидка доставка Новою Поштою по всій Україні.",
                WeightKg = weight,
                IsActive = true,
                CreatedAt = now.AddDays(-20 + (i % 15)),
                UpdatedAt = now
            };

            var images = new List<ProductImage>();
            var sortOrder = 1;

            if (!string.IsNullOrEmpty(item.MainImage))
            {
                images.Add(new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    Url = item.MainImage,
                    Alt = $"{item.Name} — головне фото",
                    SortOrder = sortOrder++
                });
            }

            if (item.Gallery != null)
            {
                foreach (var galleryUrl in item.Gallery)
                {
                    if (!string.IsNullOrEmpty(galleryUrl) && galleryUrl != item.MainImage)
                    {
                        images.Add(new ProductImage
                        {
                            Id = Guid.NewGuid(),
                            ProductId = productId,
                            Url = galleryUrl,
                            Alt = $"{item.Name} — детальне фото {sortOrder}",
                            SortOrder = sortOrder++
                        });
                    }
                }
            }

            product.Images = images;
            context.Products.Add(product);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private sealed class YalynkaItemDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("raw_name")]
        public string RawName { get; set; } = string.Empty;

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonPropertyName("category_name")]
        public string CategoryName { get; set; } = string.Empty;

        [JsonPropertyName("category_slug")]
        public string CategorySlug { get; set; } = string.Empty;

        [JsonPropertyName("supplier_model")]
        public string SupplierModel { get; set; } = string.Empty;

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("old_price")]
        public decimal? OldPrice { get; set; }

        [JsonPropertyName("drop_price")]
        public decimal DropPrice { get; set; }

        [JsonPropertyName("main_image")]
        public string? MainImage { get; set; }

        [JsonPropertyName("gallery")]
        public List<string>? Gallery { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("attributes")]
        public Dictionary<string, string> Attributes { get; set; } = new();
    }
}
