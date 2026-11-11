# Документация: Админ-панель, Аналитика и Финансы партнеров (Admin API)

Данный документ содержит полное описание архитектуры, финансовых формул и REST API эндпоинтов административной панели интернет-магазина.

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

## 2. Финансовая модель и алгоритм распределения прибыли

Модуль партнерских финансов разработан для прозрачного учета вложений 4 партнеров-фаундеров и расчета выплат.

### Базовые формулы:
1. **Валовая прибыль (Gross Profit)**:
   $$\text{GrossProfit} = \text{TotalRevenue} - \text{TotalDropCost}$$
   *Рассчитывается по всем действительным заказам (исключая статусы `Cancelled`, `Refused`, `Returned`).*
2. **Чистая прибыль к распределению (Net Profit to Distribute)**:
   $$\text{NetProfitToDistribute} = \max(0, \text{GrossProfit} - \text{TotalAdSpendActual})$$
   *Из прибыли от продаж в первую очередь окупаются реальные расходы на рекламу.*
3. **Остаток рекламного бюджета (Remaining Ad Budget)**:
   $$\text{RemainingAdBudget} = \text{AdBudgetInvested} - \text{TotalAdSpendActual}$$
4. **Доля партнера ($\text{SharePercent}_i$)**:
   - **Фиксированная договоренность (Equity)**: если за партнером закреплен процент (например, за разработку бэкенда и фронтенда).
   - **Динамическая доля от вклада**: если процент не зафиксирован жестко, бэкенд рассчитывает его пропорционально внесенным депозитам:
     $$\text{SharePercent}_i = \frac{\text{InvestedAmount}_i}{\text{AdBudgetInvested}} \times 100\%$$
5. **Текущая выплата партнеру ($\text{CurrentPayout}_i$)**:
   $$\text{CurrentPayout}_i = \text{NetProfitToDistribute} \times \left(\frac{\text{SharePercent}_i}{100}\right)$$
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
| `GET` | `/api/admin/finance/partners-summary` | Сводка финансового состояния и персональные выплаты / ROI для каждого партнера команды. |
| `POST` | `/api/admin/finance/deposits` | Внесение депозита в общий рекламный банк (`amount`, `date`, `note`, `adminId`). |
| `GET` | `/api/admin/finance/deposits` | История всех внесенных депозитов. |
| `POST` | `/api/admin/finance/ad-spend` | Фиксация расхода на рекламу (`amount`, `source`, `date`, `note`). |
| `GET` | `/api/admin/finance/ad-spend` | История списаний маркетингового бюджета. |
| `DELETE` | `/api/admin/finance/ad-spend/{id}` | Удаление записи расхода. |
| `PATCH` | `/api/admin/finance/partners/{adminId}` | Обновление роли партнера или его фиксированного процента. |

---

## 4. База данных: новые таблицы и сущности

В миграции `20261111183347_AddPartnerFinanceAndAdSpend` добавлены:

1. **`PartnerDeposits`**:
   - `Id` (UUID, PK)
   - `AdminId` (UUID, FK к `Admins` ON DELETE CASCADE)
   - `Amount` (numeric(12,2))
   - `Date` (timestamptz)
   - `Note` (text)
   - `CreatedAt` (timestamptz)
2. **`AdSpends`**:
   - `Id` (UUID, PK)
   - `Amount` (numeric(12,2))
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

Все запросы готовы к выполнению в файле [`EcommerceBackend.http`](file:///c:/Users/danii/OneDrive/Робочий%20стіл/backend/back-web/EcommerceBackend.http):
- Запросы **35–40**: Вход администратора, получение токена, смена пароля.
- Запросы **46–49**: Админские товары (CRUD, быстрое обновление остатка, мягкое удаление).
- Запросы **50–54**: Админские заказы (фильтры по датам, деталка с себестоимостью, смена статуса с возвратом на склад).
- Запросы **55–60**: Дашборд аналитики (KPI, графики, воронка, топ товаров, остатки).
- Запросы **61–65**: Финансы партнеров (сводка, внесение депозита, списание на рекламу).
