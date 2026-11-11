# Документация: Админ-панель, Аналитика и Финансы партнеров (Admin API)

Данный документ содержит полное описание архитектуры, финансовых формул, мультивалютного пересчета (UAH / USD) и REST API эндпоинтов административной панели интернет-магазина.

---

## 1. Архитектура и разделение контуров API

В системе реализовано строгое разделение на **два контура**:

```mermaid
flowchart TD
    subgraph ClientZone["Публичный контур (Storefront / Покупатели)"]
        Client[Покупатель] -->|GET /api/products| Catalog[Каталог товаров (только IsActive)]
        Client -->|POST /api/orders| Checkout[Оформление гостевого заказа]
        Client -->|GET /api/orders/by-public-id| OrderThankYou[Экран «Спасибо за заказ»]
    end

    subgraph AdminZone["Защищенный контур (Admin Panel / Команда)"]
        AdminUser[Администратор / Партнер] -->|POST /api/admin/auth/login| Login[Получение JWT токена]
        Login --> BearerHeader[Заголовок Authorization: Bearer JWT]
        BearerHeader --> AdminProducts[AdminProducts: /api/admin/products]
        BearerHeader --> AdminOrders[AdminOrders: /api/admin/orders]
        BearerHeader --> AdminAnalytics[AdminAnalytics: /api/admin/analytics]
        BearerHeader --> AdminFinance[AdminFinance: /api/admin/finance]
    end

    subgraph ExternalServices["Внешние интеграции"]
        NbuApi["API Национального Банка Украины (НБУ)"] -->|Официальный курс USD/UAH| CurrencyService[NbuCurrencyRateService]
        CurrencyService --> AdminFinance
    end
```

1. **Публичный контур витрины (`/api/...`)**:
   - Покупатели работают без регистрации (гостевой чекаут).
   - Каталог `GET /api/products` возвращает исключительно активные товары (`IsActive = true`).
   - При оформлении заказа `POST /api/orders` проверяются и резервируются складские остатки с пессимистической блокировкой `SELECT ... FOR UPDATE`, генерируется безопасный случайный `PublicId`.
   - Клиент отслеживает свой заказ только по `PublicId` (`/api/orders/by-public-id/{publicId}`).

2. **Защищенный административный контур (`/api/admin/...`)**:
   - Доступ защищен атрибутом `[Authorize(Roles = "Admin")]`.
   - Включает полный цикл управления каталогом, заказами, складом, маркетинговым бюджетом и финансовой аналитикой.

---

## 2. Финансовая модель, мультивалютность (ГРН / USD) и защита от инфляции

Модуль партнерских финансов разработан для прозрачного учета вложений 4 партнеров-фаундеров и расчета выплат с учетом инфляции и валютных колебаний курса гривны.

### Интеграция с Национальным Банком Украины (НБУ):
- Сервис [`NbuCurrencyRateService`](file:///c:/Users/danii/OneDrive/Робочий%20стіл/backend/back-web/Application/Services/NbuCurrencyRateService.cs) в реальном времени подтягивает официальный курс доллара США (`USD`) с государственного API НБУ:
  `https://bank.gov.ua/NBUStatService/v1/statdirectory/exchange?valcode=USD&json`
- Для защиты от сетевых сбоев курс кэшируется в памяти, а при отсутствии связи используется безопасный fallback.
- При сохранении депозита или списания фиксируется точный курс на дату операции.

### Защита от инфляции при динамических долях:
Если один партнер внес 10 000 грн в ноябре (при курсе 41.50 грн = $240.96), а второй партнер внес 10 000 грн позже при курсе 45.00 грн ($222.22), система пересчитывает доли **в долларовом эквиваленте (USD)**. Таким образом, партнер, вложивший деньги раньше, защищен от девальвации.

### Базовые формулы:
1. **Валовая прибыль (Gross Profit)**:
   $$\text{GrossProfit} = \text{TotalRevenue} - \text{TotalDropCost}$$
   $$\text{GrossProfitUsd} = \frac{\text{GrossProfit}}{\text{UsdRate}}$$
   *Рассчитывается по всем действительным заказам (исключая статусы `Cancelled`, `Refused`, `Returned`).*
2. **Чистая прибыль к распределению (Net Profit to Distribute)**:
   $$\text{NetProfitToDistribute} = \max(0, \text{GrossProfit} - \text{TotalAdSpendActual})$$
   $$\text{NetProfitToDistributeUsd} = \max(0, \text{GrossProfitUsd} - \text{TotalAdSpendActualUsd})$$
   *Из прибыли от продаж в первую очередь окупаются реальные расходы на рекламу.*
3. **Остаток рекламного бюджета (Remaining Ad Budget)**:
   $$\text{RemainingAdBudget} = \text{AdBudgetInvested} - \text{TotalAdSpendActual}$$
   $$\text{RemainingAdBudgetUsd} = \text{AdBudgetInvestedUsd} - \text{TotalAdSpendActualUsd}$$
4. **Доля партнера ($\text{SharePercent}_i$)**:
   - **Фиксированная договоренность (Equity)**: если за партнером закреплен фиксированный процент (например, 35%, 35%, 20%, 10%).
   - **Динамическая доля от вклада**: если процент не зафиксирован жестко, бэкенд рассчитывает его по сумме долларового эквивалента:
     $$\text{SharePercent}_i = \frac{\text{InvestedAmountUsd}_i}{\text{AdBudgetInvestedUsd}} \times 100\%$$
5. **Текущая выплата партнеру ($\text{CurrentPayout}_i$)**:
   $$\text{CurrentPayout}_i = \text{NetProfitToDistribute} \times \left(\frac{\text{SharePercent}_i}{100}\right)$$
   $$\text{CurrentPayoutUsd}_i = \text{NetProfitToDistributeUsd} \times \left(\frac{\text{SharePercent}_i}{100}\right)$$
6. **Окупаемость инвестиций ($\text{ROI}_i$)**:
   $$\text{ROI}_i = \frac{\text{CurrentPayout}_i - \text{InvestedAmount}_i}{\text{InvestedAmount}_i} \times 100\%$$

---

## 3. Справочник API эндпоинтов

### 3.1. Аутентификация администраторов (`/api/admin/auth`)

| Метод | URL | Описание | Доступ |
|---|---|---|---|
| `POST` | `/api/admin/auth/login` | Вход по логину (`username`) и паролю. Возвращает JWT токен. | Public |
| `GET` | `/api/admin/auth/me` | Данные текущего вошедшего администратора. | Bearer |
| `POST` | `/api/admin/auth/change-password` | Смена собственного пароля. | Bearer |
| `POST` | `/api/admin/auth/admins` | Регистрация нового администратора/менеджера. | Admin |
| `DELETE` | `/api/admin/auth/admins/{id}` | Удаление администратора с защитой от удаления себя и последнего админа. | Admin |

### 3.2. Управление товарами (`/api/admin/products`)

| Метод | URL | Описание |
|---|---|---|
| `GET` | `/api/admin/products` | Список товаров с пагинацией, фильтрацией (`categoryId`, `isActive`) и поиском (`search`). Показывает в том числе скрытые товары. |
| `GET` | `/api/admin/products/{id}` | Полные данные товара для формы редактирования. |
| `POST` | `/api/admin/products` | Создание нового товара. |
| `PUT` | `/api/admin/products/{id}` | Полное обновление товара. |
| `PATCH` | `/api/admin/products/{id}/stock` | Быстрое обновление остатка (`{ "stockQuantity": 100 }`). |
| `DELETE` | `/api/admin/products/{id}` | Мягкое удаление (деактивация `IsActive = false`). |

### 3.3. Управление заказами (`/api/admin/orders`)

| Метод | URL | Описание |
|---|---|---|
| `GET` | `/api/admin/orders` | Список всех заказов с пагинацией, фильтрами по статусу (`status`), датам (`from`, `to`) и поиском (`search`). |
| `GET` | `/api/admin/orders/{id}` | Полная карточка заказа: клиент, город и склад Новой Почты, позиции с дроп-ценой, общая прибыль, история статусов и комментарий. |
| `PATCH` | `/api/admin/orders/{id}/status` | Смена статуса заказа. Записывает событие в историю с ID админа. При смене на `Cancelled` **автоматически возвращает товар на склад**. |
| `PATCH` | `/api/admin/orders/{id}/comment` | Обновление внутреннего служебного комментария администратора. |

### 3.4. Аналитический дашборд (`/api/admin/analytics`)

| Метод | URL | Параметры | Описание |
|---|---|---|---|
| `GET` | `/api/admin/analytics/summary` | `period`: `today`, `yesterday`, `7d`, `30d`, `season` | Сводные KPI: Выручка, Чистый профит, Заказы, Средний чек, Конверсия оплат со сравнением с прошлым периодом. |
| `GET` | `/api/admin/analytics/charts/sales` | `period`: `7d`, `30d`, `season`, `interval`: `day` | Массив точек по дням (`date`, `ordersCount`, `revenue`, `profit`) для графиков. |
| `GET` | `/api/admin/analytics/orders-breakdown` | — | Воронка заказов по статусам и распределение по способам оплаты (`CashOnDelivery` vs `Online`). |
| `GET` | `/api/admin/analytics/top-products` | `limit` (по умолчанию 5) | Топ наиболее продаваемых позиций по выручке и числу продаж. |
| `GET` | `/api/admin/analytics/low-stock` | `threshold` (по умолчанию 5) | Товары с критическим остатком $\le 5$ шт с указанием поставщика. |

### 3.5. Финансы и партнерские доли (`/api/admin/finance`)

| Метод | URL | Описание |
|---|---|---|
| `GET` | `/api/admin/finance/exchange-rate` | **Официальный курс доллара НБУ** в реальном времени (`rate`, `date`, `source`). |
| `GET` | `/api/admin/finance/partners-summary` | **Сводка в двух валютах (UAH и USD)**: выручка, себестоимость, прибыль, рекламный банк, факт рекламы, выплаты и ROI для каждого партнера. |
| `POST` | `/api/admin/finance/deposits` | Внесение депозита в рекламный банк (`amount`, `currency`: `"UAH"` или `"USD"`, `date`, `note`, `adminId`). Автоматически фиксирует курс валют. |
| `GET` | `/api/admin/finance/deposits` | История всех внесенных депозитов с суммами в UAH, USD и зафиксированным курсом. |
| `POST` | `/api/admin/finance/ad-spend` | Фиксация расхода на рекламу (`amount`, `currency`: `"UAH"` или `"USD"`, `source`, `date`, `note`). |
| `GET` | `/api/admin/finance/ad-spend` | История списаний маркетингового бюджета. |
| `DELETE` | `/api/admin/finance/ad-spend/{id}` | Удаление записи расхода. |
| `PATCH` | `/api/admin/finance/partners/{adminId}` | Обновление роли партнера или его фиксированного процента. |

---

## 4. База данных: новые таблицы и сущности

В миграциях добавлены:

1. **`PartnerDeposits`**:
   - `Id` (UUID, PK)
   - `AdminId` (UUID, FK к `Admins` ON DELETE CASCADE)
   - `Amount` (numeric(12,2)) — сумма в гривне
   - `AmountUsd` (numeric(12,2)) — эквивалент в USD на момент депозита
   - `ExchangeRate` (numeric(8,4)) — курс доллара НБУ на момент операции
   - `Date` (timestamptz)
   - `Note` (text)
   - `CreatedAt` (timestamptz)
2. **`AdSpends`**:
   - `Id` (UUID, PK)
   - `Amount` (numeric(12,2)) — сумма в гривне
   - `AmountUsd` (numeric(12,2)) — эквивалент в USD
   - `ExchangeRate` (numeric(8,4)) — курс доллара
   - `Source` (varchar(100), например `Google Shopping`, `Meta Ads`)
   - `Date` (timestamptz)
   - `Note` (text)
   - `RecordedByAdminId` (UUID, FK к `Admins` ON DELETE SET NULL)
   - `CreatedAt` (timestamptz)
3. **Расширение `Admins`**:
   - `PartnerRole` (varchar(100), например `Lead / Dev`, `Dev / Backend`, `Marketing`, `Operations`)
   - `FixedSharePercent` (numeric(5,2))

---

## 5. Инструкция по тестированию

Все запросы с реальными ID из базы Supabase готовы к выполнению в файле [`EcommerceBackend.http`](file:///c:/Users/danii/OneDrive/Робочий%20стіл/backend/back-web/EcommerceBackend.http):
- Запросы **1–7**: Категории и поставщики (CRUD, проверка конфликтов).
- Запросы **8–15**: Каталог товаров витрины.
- Запросы **16–18**: Оформление заказа клиентом и страница «Спасибо за заказ».
- Запросы **19–23**: JWT авторизация администраторов.
- Запросы **24–27**: Админка товаров (CRUD, быстрое обновление остатка, мягкое удаление).
- Запросы **28–32**: Админка заказов (фильтры по датам, деталка с себестоимостью, смена статуса с возвратом на склад).
- Запросы **33–38**: Дашборд аналитики (KPI, графики, воронка, топ товаров, остатки).
- Запросы **39–44**: Финансы партнеров (курс НБУ, сводка UAH/USD, внесение депозита, списание на рекламу).
