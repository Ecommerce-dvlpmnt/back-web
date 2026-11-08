# План и валидация индексов каталога (PostgreSQL & EF Core)

## 1. Список созданных индексов и их обоснование

Таблица: `public."Products"`

| Имя индекса | Метод | Столбцы / Выражение | Частичный фильтр (Predicate) | Обоснование и сценарий использования |
|---|---|---|---|---|
| `Products_Slug_key` | **B-tree** (UNIQUE) | `("Slug")` | — | Быстрый поиск карточки товара по URL-slug (`/products/{slug}`). Уникальность предотвращает дублирование страниц и гарантирует консистентность маршрутизации. |
| `Products_Sku_key` | **B-tree** (UNIQUE) | `("Sku")` | — | Быстрый точный поиск по артикулу (SKU). Уникальность на уровне БД для складского учёта. |
| `IX_Products_Catalog_PriceDesc` | **B-tree** | `("CategoryId" ASC, "Price" DESC)` | `WHERE ("IsActive" IS TRUE)` | **Основной каталог товаров с сортировкой по цене убывания (`price_desc`).** Частичный индекс (только активные товары) экономит память и диск, отсекая неактивные/архивные товары. |
| `IX_Products_Catalog_CreatedAt` | **B-tree** | `("CategoryId" ASC, "CreatedAt" DESC)` | `WHERE ("IsActive" IS TRUE)` | Каталог по умолчанию (`date_desc`): вывод новинок выбранной категории без необходимости в явной сортировке `Sort Method: quicksort` в памяти. |
| `IX_Products_Price` | **B-tree** | `("Price")` | `WHERE ("IsActive" IS TRUE)` | Общий листинг каталога без привязки к конкретной категории (например, «Все товары» с фильтром/сортировкой по цене). |
| `IX_Products_Attributes_gin` | **GIN** (`jsonb_path_ops`) | `("Attributes" jsonb_path_ops)` | — | **Поиск по динамическим характеристикам товара (JSONB).** Использование операторного класса `jsonb_path_ops` оптимизировано под оператор включения `@>`. Данный класс на 30–50% компактнее и быстрее стандартного GIN (`jsonb_ops`), так как хеширует полные пути ключ-значение. |
| `IX_Products_Name_trgm` | **GIN** (`gin_trgm_ops`) | `("Name" gin_trgm_ops)` | — | Полнотекстовый/подстрочный поиск `ILIKE '%...%'` по наименованию товара без full table scan. Требует расширения `pg_trgm`. |
| `IX_Products_Sku_trgm` | **GIN** (`gin_trgm_ops`) | `("Sku" gin_trgm_ops)` | — | Неточный / частичный поиск по артикулу `ILIKE '%...%'`. |
| `IX_Products_SupplierId` | **B-tree** | `("SupplierId")` | — | Быстрая фильтрация товаров по поставщику (админка / синхронизация остатков). |
| `PK_Products` | **B-tree** (UNIQUE) | `("Id")` | — | Первичный ключ таблицы (UUID). |

> **Примечание по мультитенантности (`TenantId`):**  
> В текущей доменной модели сущность `Product` не содержит поля `TenantId` (архитектура моно-арендная). В случае внедрения многопользовательской/мультитенантной изоляции рекомендуется составной индекс:  
> `("TenantId", "CategoryId", "Price" DESC) WHERE ("IsActive" IS TRUE)` — где `TenantId` ставится первым полем для жесткого секционирования данных тенанта на уровне B-Tree.

---

## 2. Примеры сырых SQL-запросов

### 2.1. Быстрый поиск по Slug (страница товара)
```sql
SELECT *
FROM "Products"
WHERE "Slug" = 'iphone-15-pro-max'
LIMIT 1;
```

### 2.2. Каталог: фильтрация по категории + сортировка по цене (убывание)
```sql
SELECT "Id", "Name", "Price", "CategoryId", "CreatedAt"
FROM "Products"
WHERE "CategoryId" = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
  AND "IsActive" IS TRUE
ORDER BY "Price" DESC
LIMIT 20 OFFSET 0;
```

### 2.3. Поиск по JSONB-характеристикам (`Attributes`)
```sql
-- 1. Проверка вхождения через оператор @> (поддерживается IX_Products_Attributes_gin с jsonb_path_ops)
SELECT "Id", "Name", "Price", "Attributes"
FROM "Products"
WHERE "Attributes" @> '{"color": "black", "memory": "256GB"}';

-- 2. Проверка наличия ключа через оператор ?
-- Примечание: для операторов ?, ?|, ?& требуется индекс с дефолтным jsonb_ops.
-- Для jsonb_path_ops используется проверка вхождения пути: @> '{"color": null}'
SELECT "Id", "Name", "Attributes"
FROM "Products"
WHERE "Attributes" ? 'brand';
```

---

## 3. Реальные планы выполнения (EXPLAIN ANALYZE из Supabase)

Замеры проведены на реальной базе данных Supabase PostgreSQL 17:

### 3.1. Поиск по Slug
**Запрос:**
```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM "Products"
WHERE "Slug" = 'sample-product-slug';
```
**Фактический план:**
```text
Index Scan using "Products_Slug_key" on "Products"  (cost=0.14..2.36 rows=1 width=1735) (actual time=0.037..0.037 rows=0 loops=1)
  Index Cond: (("Slug")::text = 'sample-product-slug'::text)
  Buffers: shared hit=1
Planning Time: 0.090 ms
Execution Time: 0.059 ms
```
*Результат:* Мгновенный точечный `Index Scan` за **0.059 ms**, 1 чтение буфера.

---

### 3.2. Фильтрация по категории + Сортировка по цене
**Запрос:**
```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM "Products"
WHERE "CategoryId" = '00000000-0000-0000-0000-000000000000'
  AND "IsActive" IS TRUE
ORDER BY "Price" DESC
LIMIT 20;
```
**Фактический план:**
```text
Limit  (cost=0.14..2.36 rows=1 width=1735) (actual time=1.890..1.891 rows=0 loops=1)
  Buffers: shared read=1
  ->  Index Scan using "IX_Products_Catalog_PriceDesc" on "Products"  (cost=0.14..2.36 rows=1 width=1735) (actual time=1.888..1.889 rows=0 loops=1)
        Index Cond: ("CategoryId" = '00000000-0000-0000-0000-000000000000'::uuid)
        Buffers: shared read=1
Planning:
  Buffers: shared hit=257
Planning Time: 0.586 ms
Execution Time: 1.917 ms
```
*Результат:* `Index Scan using "IX_Products_Catalog_PriceDesc"`. Узел `Sort` исключён из плана выполнения — строки возвращаются сразу в отсортированном порядке из индекса.

---

### 3.3. Поиск по характеристикам через GIN (`jsonb_path_ops`)
**Запрос:**
```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM "Products"
WHERE "Attributes" @> '{"color": "black"}';
```
**Фактический план:**
```text
Bitmap Heap Scan on "Products"  (cost=2.73..3.84 rows=1 width=1735) (actual time=0.011..0.011 rows=0 loops=1)
  Recheck Cond: ("Attributes" @> '{"color": "black"}'::jsonb)
  Buffers: shared hit=2
  ->  Bitmap Index Scan on "IX_Products_Attributes_gin"  (cost=0.00..2.73 rows=1 width=0) (actual time=0.008..0.008 rows=0 loops=1)
        Index Cond: ("Attributes" @> '{"color": "black"}'::jsonb)
        Buffers: shared hit=2
Planning:
  Buffers: shared hit=1
Planning Time: 0.122 ms
Execution Time: 0.037 ms
```
*Результат:* `Bitmap Index Scan on "IX_Products_Attributes_gin"`. Исключает Seq Scan при поиске по любым JSONB-свойствам за **0.037 ms**.

---

## 4. Почему на небольших таблицах планировщик может выбирать Seq Scan

В PostgreSQL стоимостной оптимизатор (cost-based optimizer) рассчитывает стоимость ввода-вывода (I/O) и CPU:
- На пустой или тестовой таблице (например, 18 строк, занимающих всего 1–2 страницы памяти `Buffers: shared hit=2`) последовательное чтение (`Seq Scan`) обходится планировщику дешевле (`cost ~ 0.00..2.23`), чем чтение индексного B-Tree блока + случайный переход к куче (`heap lookup`).
- При росте таблицы до сотен/тысяч строк стоимость `Seq Scan` линейно возрастает, и оптимизатор автоматически переключается на созданные индексы (`Index Scan` / `Bitmap Index Scan`), как продемонстрировано в планах выше.
