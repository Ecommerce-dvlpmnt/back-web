using System.Text.Json;
using Bogus;
using EcommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Data;

// DbSeeder наполняет базу демо-данными ТОЛЬКО если соответствующие таблицы пустые.
// Вызывается из Program.cs в режиме Development — чтобы на дев-машинах проект сразу был "живой".
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext context, CancellationToken cancellationToken = default)
    {
        await SeedSuppliersAsync(context, cancellationToken);
        var categoryIds = await SeedCategoriesAsync(context, cancellationToken);
        await SeedProductsAsync(context, categoryIds, cancellationToken);
    }

    // 1) Поставщики: 3 дефолтных, если таблица пуста.
    private static async Task SeedSuppliersAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Suppliers.AnyAsync(cancellationToken))
            return;

        context.Suppliers.AddRange(
            new Supplier
            {
                Id = Guid.NewGuid(),
                Name = "Ёлочка-Трейд",
                Contact = "+380501234567",
                City = "Харьков",
                Notes = "Основной поставщик игрушек и декора, отсрочка оплаты 14 дней",
                IsActive = true
            },
            new Supplier
            {
                Id = Guid.NewGuid(),
                Name = "Confezione UA",
                Contact = "+380671112233",
                City = "Львов",
                Notes = "Сладкие наборы и сувениры, предоплата 100%",
                IsActive = true
            },
            new Supplier
            {
                Id = Guid.NewGuid(),
                Name = "Снежный Лес",
                Contact = "@snowforest_tm (Telegram)",
                City = "Киев",
                Notes = "Гирлянды и свет, приём остатков по будням после 15:00",
                IsActive = true
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    // 2) Базовые корневые категории (в тему проекта), если таблица пуста. Возвращает их Id для привязки товаров.
    private static async Task<List<Guid>> SeedCategoriesAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Categories.AnyAsync(cancellationToken))
            return await context.Categories.Select(c => c.Id).ToListAsync(cancellationToken);

        var roots = new (string Name, string Slug)[]
        {
            ("Ёлочные игрушки", "christmas-ornaments"),
            ("Гирлянды и свет", "garlands"),
            ("Текстиль и уют", "cozy-textile"),
            ("Подарки и сувениры", "gifts")
        };

        var categories = roots.Select((root, index) => new Category
        {
            Id = Guid.NewGuid(),
            Name = root.Name,
            Slug = root.Slug,
            ParentId = null,
            SortOrder = index,
            IsActive = true,
            FilterSchema = "{}",
            MetaTitle = root.Name + " — Новый год"
        }).ToList();

        context.Categories.AddRange(categories);
        await context.SaveChangesAsync(cancellationToken);

        return categories.Select(c => c.Id).ToList();
    }

    // 3) Товары: 18 штук через Faker<Product> — имена тематические, цены/остатки/картинки рандомные.
    private static async Task SeedProductsAsync(
        AppDbContext context,
        List<Guid> categoryIds,
        CancellationToken cancellationToken)
    {
        if (await context.Products.AnyAsync(cancellationToken))
            return;

        var supplierIds = await context.Suppliers.Select(s => s.Id).ToListAsync(cancellationToken);
        if (supplierIds.Count == 0 || categoryIds.Count == 0)
            return;

        // Имя + slug идут парами: slug гарантированно уникальный (уникальный индекс в БД).
        var catalog = new (string Name, string Slug)[]
        {
            ("Шар стеклянный «Зимняя сказка» 8 см", "shar-zimnyaya-skazka"),
            ("Шар пластиковый «Мятный коктейль» 6 см", "shar-myatnyj-koktejl"),
            ("Шар деревянный «Хохлома» 7 см", "shar-hohloma"),
            ("Фигурка «Дед Мороз» 20 см", "figurka-ded-moroz"),
            ("Подвеска «Снежный гномик» 10 см", "podveska-gnomik"),
            ("Шар коллекционный «Ретро СССР» 12 см", "shar-retro-ussr"),
            ("Гирлянда-занавес 3×3 м, 300 LED", "gerlyanda-zanaves"),
            ("Гирлянда «Свечи» на батарейках 2 м", "gerlyanda-sveshi"),
            ("Световая фигура «Снежинка» IP44 50 см", "figura-snezhinka"),
            ("Скатерть «Северные олени» 150×220", "skatert-oleni"),
            ("Тёплые новогодние носки (2 шт)", "noski-ny"),
            ("Набор конфет «Мандариновый вечер» 500 г", "konfety-mandarinki"),
            ("Магнит «С Новым годом» (набор 4 шт)", "magnit-ny"),
            ("Венок новогодний с шишками 40 см", "venok-s-shishkami"),
            ("Топпер «Счастливого Нового года»", "topper-novogo-goda"),
            ("Носки термоёлочные (3 пары)", "noski-termoelki"),
            ("Свеча «Корично-апельсиновая»", "svecha-koricca"),
            ("Упаковочная бумага «Крафт-Снежинки»", "bumaga-kraft")
        };

        // Проверенные живые фото Unsplash (остальные ID_answers 404 — поэтому пул маленький, зато рабочий).
        const string unsplashBase = "https://images.unsplash.com/photo-";
        string[] unsplashVerified =
        [
            "1512403754473-27835f7b9984",
            "1520531158340-44015069e78e",
            "1600585154340-be6161a56a0c"
        ];

        var faker = new Faker("ru");
        var products = new List<Product>(catalog.Length);

        for (var i = 0; i < catalog.Length; i++)
        {
            var (name, slug) = catalog[i];
            var price = faker.Random.Decimal(99m, 2499m);

            products.Add(new Faker<Product>("ru")
                .RuleFor(p => p.Id, _ => Guid.NewGuid())
                .RuleFor(p => p.CategoryId, _ => faker.PickRandom(categoryIds))
                .RuleFor(p => p.SupplierId, _ => faker.PickRandom(supplierIds))
                .RuleFor(p => p.Sku, _ => $"NY-{(i + 1):D4}")
                .RuleFor(p => p.Name, _ => name)
                .RuleFor(p => p.Slug, _ => slug)
                .RuleFor(p => p.Description, f => f.Lorem.Sentence(14))
                .RuleFor(p => p.DropPrice, f => Math.Round(price * 0.6m, 2))
                .RuleFor(p => p.Price, _ => price)
                .RuleFor(p => p.OldPrice, f => f.Random.Bool(0.3f) ? Math.Round(price * 1.2m, 2) : null)
                .RuleFor(p => p.Stock, f => f.Random.Int(5, 80))
                // Bogus выдаёт даты с Kind=Local, а Npgsql требует UTC для timestamptz — принудительно помечаем.
                .RuleFor(p => p.StockUpdatedAt, f => DateTime.SpecifyKind(f.Date.Recent(20), DateTimeKind.Utc))
                // Главное фото: 30% — Unsplash, 70% — Picsum с seed по слагу (детерминированно и всегда живое).
                .RuleFor(p => p.MainImageUrl, (f, p) => f.Random.Bool(0.3f)
                    ? $"{unsplashBase}{f.PickRandom(unsplashVerified)}?auto=format&fit=crop&w=800&q=60"
                    : $"https://picsum.photos/seed/ny{p.Slug}/800/600")
                // Галерея: два дополнительных фото в ProductImages (SortOrder — порядок в слайдере).
                .RuleFor(p => p.Images, (f, p) => new List<ProductImage>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        Url = $"https://picsum.photos/seed/ny{p.Slug}-1/800/600",
                        Alt = $"{name} — общий вид",
                        SortOrder = 1
                    },
                    new()
                    {
                        Id = Guid.NewGuid(),
                        Url = $"https://picsum.photos/seed/ny{p.Slug}-2/800/600",
                        Alt = $"{name} — детализация",
                        SortOrder = 2
                    }
                })
                .RuleFor(p => p.Attributes, f => JsonSerializer.Serialize(new
                {
                    material = f.PickRandom("glass", "plastic", "wood", "textile"),
                    weightG = f.Random.Int(50, 900)
                }))
                .RuleFor(p => p.MetaTitle, _ => name)
                .RuleFor(p => p.MetaDescription, f => $"{name} — купить с доставкой по Украине к Новому году.")
                .RuleFor(p => p.WeightKg, f => f.Random.Decimal(0.05m, 2.5m))
                .RuleFor(p => p.IsActive, _ => true)
                .RuleFor(p => p.CreatedAt, f => DateTime.SpecifyKind(f.Date.Past(1), DateTimeKind.Utc))
                .RuleFor(p => p.UpdatedAt, _ => DateTime.UtcNow)
                .Generate());
        }

        context.Products.AddRange(products);
        await context.SaveChangesAsync(cancellationToken);
    }
}
