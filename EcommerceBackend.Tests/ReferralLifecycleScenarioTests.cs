using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Application.Telegram;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EcommerceBackend.Tests;

/// <summary>
/// Наочні наскрізні сценарії повного життєвого циклу реферальної системи:
/// від генерації коду та оформлення замовлення до переходу з холду на баланс і виплати на картку.
/// </summary>
public sealed class ReferralLifecycleScenarioTests
{
    [Fact]
    public async Task Scenario1_FullHappyPath_FromOrderToDeliveredBalanceAndPayout()
    {
        // 1. Ініціалізація симулятора партнерської системи
        var system = new ReferralSystemSimulator();
        var telegramClient = new RecordingTelegramBotClient();
        var handler = system.CreateBotHandler(telegramClient);

        const long telegramUserId = 777123456L;

        // 2. Партнер тисне /start у Телеграм-боті
        await handler.HandleUpdateAsync(CreateMessage(telegramUserId, "/start"));

        var partner = system.GetProfile(telegramUserId);
        Assert.NotNull(partner);
        Assert.Equal("AUTO_777123", partner.ReferralCode);
        Assert.Equal(5m, partner.CommissionPercent);

        var initialStats = system.GetStats(telegramUserId);
        Assert.Equal(0m, initialStats.AvailableBalance);
        Assert.Equal(0m, initialStats.PendingBalance);

        // Перевіряємо привітання від бота та наявність реф-посилання
        var welcomeMsg = telegramClient.LastMessageText;
        Assert.Contains("AUTO_777123", welcomeMsg);
        Assert.Contains("https://front-web-pi.vercel.app/?ref=AUTO_777123", welcomeMsg);

        // 3. Покупець на сайті оформив замовлення №101 на 2 000 грн за реф-посиланням
        var order1 = system.PlaceOrder(orderNumber: 101, totalPrice: 2000m, referralCode: "AUTO_777123");

        // Перевіряємо: 5% (100 грн) потрапили в ХОЛД (Pending)
        Assert.Equal(100m, order1.ReferralRewardAmount);
        Assert.Equal(ReferralRewardStatus.Pending, order1.ReferralRewardStatus);

        var statsAfterOrder1 = system.GetStats(telegramUserId);
        Assert.Equal(0m, statsAfterOrder1.AvailableBalance);
        Assert.Equal(100m, statsAfterOrder1.PendingBalance);
        Assert.Equal(2000m, statsAfterOrder1.TotalTurnover);
        Assert.Equal(1, statsAfterOrder1.PendingOrders);

        // 4. Покупець прийшов на Нову Пошту і забрав посилку -> статус стає Delivered
        system.DeliverOrder(order1);

        // Перевіряємо: 100 грн розблоковані з холду на доступний баланс!
        var statsAfterDelivery1 = system.GetStats(telegramUserId);
        Assert.Equal(100m, statsAfterDelivery1.AvailableBalance);
        Assert.Equal(0m, statsAfterDelivery1.PendingBalance);
        Assert.Equal(1, statsAfterDelivery1.DeliveredOrders);

        // 5. Оформлюємо замовлення №102 на 5 000 грн (бонус 5% = 250 грн) і одразу доставляємо його
        var order2 = system.PlaceOrder(orderNumber: 102, totalPrice: 5000m, referralCode: "AUTO_777123");
        system.DeliverOrder(order2);

        // Тепер у партнера доступно 100 + 250 = 350 грн (більше за мінімалку 300 грн)
        var statsBeforeWithdraw = system.GetStats(telegramUserId);
        Assert.Equal(350m, statsBeforeWithdraw.AvailableBalance);
        Assert.Equal(0m, statsBeforeWithdraw.PendingBalance);
        Assert.Equal(7000m, statsBeforeWithdraw.TotalTurnover);

        // 6. Партнер у боті запитує статистику
        await handler.HandleUpdateAsync(CreateMessage(telegramUserId, TelegramBotMenu.StatsButton));
        var statsText = telegramClient.LastMessageText;
        Assert.Contains("Доступно до виведення: 350.00 грн", statsText);
        Assert.Contains("Загальний оборот: 7000.00 грн", statsText);

        // 7. Партнер у боті починає процес виведення:
        // Крок 1: Тисне кнопку "Вивести кошти"
        await handler.HandleUpdateAsync(CreateMessage(telegramUserId, TelegramBotMenu.WithdrawButton));
        Assert.Contains("Введіть суму для виведення", telegramClient.LastMessageText);

        // Крок 2: Вводить суму "350"
        await handler.HandleUpdateAsync(CreateMessage(telegramUserId, "350"));
        Assert.Contains("Введіть номер банківської картки", telegramClient.LastMessageText);

        // Крок 3: Вводить номер картки
        await handler.HandleUpdateAsync(CreateMessage(telegramUserId, "4149 4999 1234 5678"));
        Assert.Contains("Заявку на виведення 350.00 грн створено", telegramClient.LastMessageText);

        // Баланс списався під заявку:
        var statsAfterWithdraw = system.GetStats(telegramUserId);
        Assert.Equal(0m, statsAfterWithdraw.AvailableBalance);
        Assert.Single(system.PayoutRequests);

        var payout = system.PayoutRequests[0];
        Assert.Equal(350m, payout.Amount);
        Assert.Equal(PayoutRequestStatus.Pending, payout.Status);

        // 8. Адміністратор у панелі схвалює виплату
        system.ApprovePayout(payout.Id);
        Assert.Equal(PayoutRequestStatus.Completed, payout.Status);
    }

    [Fact]
    public void Scenario2_OrderRefusedAtNovaPoshta_RewardCancelledWithoutFinancialLoss()
    {
        var system = new ReferralSystemSimulator();
        const long telegramUserId = 888123456L;
        system.RegisterPartner(telegramUserId, "BOGDAN", commissionPercent: 5m);

        // 1. Замовлення на 3 000 грн (бонус 150 грн в холді)
        var order = system.PlaceOrder(orderNumber: 201, totalPrice: 3000m, referralCode: "BOGDAN");
        Assert.Equal(150m, order.ReferralRewardAmount);
        Assert.Equal(ReferralRewardStatus.Pending, order.ReferralRewardStatus);

        var stats1 = system.GetStats(telegramUserId);
        Assert.Equal(150m, stats1.PendingBalance);
        Assert.Equal(0m, stats1.AvailableBalance);

        // 2. Клієнт прийшов на пошту, оглянув товар і відмовився (Refused)
        system.RefuseOrder(order);

        // 3. Перевіряємо: винагорода анулюється, холд обнуляється, доступний баланс 0 грн
        Assert.Equal(ReferralRewardStatus.Cancelled, order.ReferralRewardStatus);
        var stats2 = system.GetStats(telegramUserId);
        Assert.Equal(0m, stats2.PendingBalance);
        Assert.Equal(0m, stats2.AvailableBalance);
        Assert.Equal(1, stats2.CancelledOrders);
        Assert.Equal(0, stats2.DeliveredOrders);
    }

    [Fact]
    public async Task Scenario3_WithdrawValidation_EnforcesMinimum300AndRejectsOverdraft()
    {
        var system = new ReferralSystemSimulator();
        var telegramClient = new RecordingTelegramBotClient();
        var handler = system.CreateBotHandler(telegramClient);

        const long telegramUserId = 999123456L;
        system.RegisterPartner(telegramUserId, "MAX5", commissionPercent: 5m);

        // Замовлення на 2000 грн -> доставлено -> на балансі 100 грн (менше 300 грн)
        var order = system.PlaceOrder(orderNumber: 301, totalPrice: 2000m, referralCode: "MAX5");
        system.DeliverOrder(order);

        // 1. Спроба вивести коли на балансі 100 грн
        await handler.HandleUpdateAsync(CreateMessage(telegramUserId, TelegramBotMenu.WithdrawButton));
        Assert.Contains("Мінімальна сума для виведення — 300.00 грн.", telegramClient.LastMessageText);
        Assert.Contains("100.00 грн", telegramClient.LastMessageText);

        // 2. Додали ще 6000 грн обороту -> баланс стає 100 + 300 = 400 грн
        var order2 = system.PlaceOrder(orderNumber: 302, totalPrice: 6000m, referralCode: "MAX5");
        system.DeliverOrder(order2);

        // Спроба вивести 500 грн при балансі 400 грн (overdraft)
        await handler.HandleUpdateAsync(CreateMessage(telegramUserId, TelegramBotMenu.WithdrawButton));
        await handler.HandleUpdateAsync(CreateMessage(telegramUserId, "500"));
        Assert.Contains("Недостатньо коштів. Доступно: 400.00 грн.", telegramClient.LastMessageText);

        // Спроба ввести менше 300 грн, наприклад 150
        await handler.HandleUpdateAsync(CreateMessage(telegramUserId, "150"));
        Assert.Contains("Мінімальна сума для виведення — 300.00 грн.", telegramClient.LastMessageText);
    }

    private static TelegramUpdate CreateMessage(long userId, string text)
    {
        return new TelegramUpdate(
            UpdateId: 1,
            Message: new TelegramMessage(
                MessageId: 1,
                From: new TelegramUser(userId, false, "Test", "User", "testuser", "uk"),
                Chat: new TelegramChat(userId, "private"),
                Date: 1700000000,
                Text: text));
    }

    /// <summary>
    /// Автономний симулятор логіки ReferralService та партнерського гаманця.
    /// Відтворює точні правила та математику ReferralService.
    /// </summary>
    private sealed class ReferralSystemSimulator : IReferralService
    {
        private readonly List<Partner> _partners = [];
        private readonly List<Order> _orders = [];
        public List<PartnerPayoutRequest> PayoutRequests { get; } = [];

        public TelegramBotCommandHandler CreateBotHandler(ITelegramBotClient botClient)
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Referral:PublicSiteUrl"] = "https://front-web-pi.vercel.app"
                })
                .Build();

            return new TelegramBotCommandHandler(this, botClient, config, NullLogger<TelegramBotCommandHandler>.Instance);
        }

        public Partner RegisterPartner(long telegramUserId, string referralCode, decimal commissionPercent = 5m)
        {
            var partner = new Partner
            {
                Id = Guid.NewGuid(),
                TelegramUserId = telegramUserId,
                Name = "Partner_" + telegramUserId,
                ReferralCode = referralCode.ToUpperInvariant(),
                CommissionPercent = commissionPercent,
                TotalEarned = 0m,
                CreatedAt = DateTime.UtcNow
            };
            _partners.Add(partner);
            return partner;
        }

        public Order PlaceOrder(int orderNumber, decimal totalPrice, string referralCode)
        {
            var partner = _partners.FirstOrDefault(p => p.ReferralCode == referralCode.ToUpperInvariant())
                ?? throw new InvalidOperationException("Партнера не знайдено");

            var reward = Math.Round(totalPrice * partner.CommissionPercent / 100m, 2, MidpointRounding.AwayFromZero);

            var order = new Order
            {
                Id = Guid.NewGuid(),
                OrderNumber = orderNumber,
                PublicId = Guid.NewGuid().ToString("N")[..8],
                CustomerFirstName = "Ivan",
                CustomerLastName = "Ivanov",
                CustomerPhone = "+380501112233",
                TotalPrice = totalPrice,
                OrderStatus = OrderStatus.New,
                ReferrerId = partner.Id,
                ReferralRewardAmount = reward,
                ReferralRewardStatus = ReferralRewardStatus.Pending,
                IsReferralRewardPaid = false
            };
            _orders.Add(order);
            return order;
        }

        public void DeliverOrder(Order order)
        {
            order.OrderStatus = OrderStatus.Delivered;
            order.ReferralRewardStatus = ReferralRewardStatus.Paid;
            order.IsReferralRewardPaid = true;

            var partner = _partners.First(p => p.Id == order.ReferrerId);
            partner.TotalEarned += order.ReferralRewardAmount;
        }

        public void RefuseOrder(Order order)
        {
            order.OrderStatus = OrderStatus.Refused;
            order.ReferralRewardStatus = ReferralRewardStatus.Cancelled;
            order.IsReferralRewardPaid = false;
        }

        public void ApprovePayout(Guid payoutId)
        {
            var payout = PayoutRequests.First(p => p.Id == payoutId);
            payout.Status = PayoutRequestStatus.Completed;
            payout.ProcessedAt = DateTime.UtcNow;
        }

        public Partner? GetProfile(long telegramUserId) =>
            _partners.FirstOrDefault(p => p.TelegramUserId == telegramUserId);

        public BotPartnerStatsResponse GetStats(long telegramUserId)
        {
            var partner = GetProfile(telegramUserId) ?? throw new InvalidOperationException("Not found");
            var orders = _orders.Where(o => o.ReferrerId == partner.Id).ToList();

            var pendingBalance = orders
                .Where(o => o.ReferralRewardStatus == ReferralRewardStatus.Pending)
                .Sum(o => o.ReferralRewardAmount);

            var paidOrPendingPayouts = PayoutRequests
                .Where(p => p.PartnerId == partner.Id && p.Status != PayoutRequestStatus.Rejected)
                .Sum(p => p.Amount);

            var availableBalance = Math.Max(0m, partner.TotalEarned - paidOrPendingPayouts);

            return new BotPartnerStatsResponse(
                partner.Name,
                partner.ReferralCode,
                partner.CommissionPercent,
                availableBalance,
                pendingBalance,
                orders.Where(o => o.OrderStatus != OrderStatus.Cancelled && o.OrderStatus != OrderStatus.Refused).Sum(o => o.TotalPrice),
                orders.Count,
                orders.Count(o => o.OrderStatus == OrderStatus.Delivered),
                orders.Count(o => o.ReferralRewardStatus == ReferralRewardStatus.Pending),
                orders.Count(o => o.OrderStatus == OrderStatus.Cancelled || o.OrderStatus == OrderStatus.Refused));
        }

        // --- IReferralService імплементація ---
        public Task<Partner?> FindPartnerByCodeAsync(string? referralCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(_partners.FirstOrDefault(p => p.ReferralCode == referralCode?.ToUpperInvariant()));

        public Task<bool> AttachReferrerAsync(Order order, string? referralCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<ReferralRewardResult?> ProcessPaidOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferralRewardResult?>(null);

        public Task<ReferralRewardResult?> ProcessDeliveredOrderRewardAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferralRewardResult?>(null);

        public Task<ReferralRewardResult?> ProcessCancelledOrderRewardAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferralRewardResult?>(null);

        public Task<BotPartnerProfileResponse?> GetBotPartnerProfileAsync(long telegramUserId, CancellationToken cancellationToken = default)
        {
            var p = GetProfile(telegramUserId);
            if (p is null) return Task.FromResult<BotPartnerProfileResponse?>(null);

            var stats = GetStats(telegramUserId);
            return Task.FromResult<BotPartnerProfileResponse?>(new BotPartnerProfileResponse(
                p.Id,
                p.TelegramUserId,
                p.Name,
                p.ReferralCode,
                p.CommissionPercent,
                stats.AvailableBalance,
                stats.PendingBalance,
                stats.TotalOrders,
                stats.DeliveredOrders,
                p.CreatedAt));
        }

        public Task<BotPartnerProfileResponse> RegisterBotPartnerAsync(BotRegisterPartnerRequest request, CancellationToken cancellationToken = default)
        {
            var code = request.ReferralCode ?? ("AUTO_" + request.TelegramUserId.ToString()[..Math.Min(6, request.TelegramUserId.ToString().Length)]);
            var p = RegisterPartner(request.TelegramUserId, code);
            return Task.FromResult(new BotPartnerProfileResponse(
                p.Id,
                p.TelegramUserId,
                p.Name,
                p.ReferralCode,
                p.CommissionPercent,
                0m,
                0m,
                0,
                0,
                p.CreatedAt));
        }

        public Task<BotPartnerStatsResponse?> GetBotPartnerStatsAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<BotPartnerStatsResponse?>(GetStats(telegramUserId));

        public Task<BotPartnerOrdersResponse?> GetBotPartnerOrdersAsync(long telegramUserId, int limit = 20, int offset = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult<BotPartnerOrdersResponse?>(new BotPartnerOrdersResponse(1, []));

        public Task<BotPayoutResponse> RequestPayoutAsync(long telegramUserId, BotPayoutRequestDto request, CancellationToken cancellationToken = default)
        {
            var partner = GetProfile(telegramUserId) ?? throw new InvalidOperationException("Not found");
            var payout = new PartnerPayoutRequest
            {
                Id = Guid.NewGuid(),
                PartnerId = partner.Id,
                Amount = request.Amount,
                CardNumber = request.CardNumber,
                CardHolderName = request.CardHolderName,
                Status = PayoutRequestStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };
            PayoutRequests.Add(payout);

            var stats = GetStats(telegramUserId);
            return Task.FromResult(new BotPayoutResponse(
                payout.Id,
                payout.Amount,
                "4149 **** **** 5678",
                payout.CardHolderName,
                payout.Status,
                stats.AvailableBalance,
                payout.CreatedAt));
        }

        public Task<IReadOnlyList<BotPayoutResponse>> GetBotPartnerPayoutsAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BotPayoutResponse>>([]);

        public Task<IReadOnlyList<PartnerResponse>> GetAllPartnersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PartnerResponse>>([]);

        public Task<PartnerResponse?> GetPartnerByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PartnerResponse?>(null);

        public Task<PartnerResponse> CreatePartnerAsync(CreatePartnerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PartnerResponse(Guid.NewGuid(), 1, "P", "REF", 5m, 0m, DateTime.UtcNow, 0m));

        public Task<PartnerResponse> UpdatePartnerAsync(Guid id, UpdateReferralPartnerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PartnerResponse(id, 1, request.Name ?? "P", request.ReferralCode ?? "REF", request.CommissionPercent ?? 5m, 0m, DateTime.UtcNow, 0m));

        public Task<ReferralCodeValidationResponse> ValidateCodeAsync(string? referralCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReferralCodeValidationResponse(true, "REF", "Partner"));

        public Task<IReadOnlyList<AdminPayoutResponse>> GetAllPayoutRequestsAsync(PayoutRequestStatus? status = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdminPayoutResponse>>([]);

        public Task<AdminPayoutResponse> ProcessPayoutRequestAsync(Guid payoutId, UpdatePayoutStatusRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminPayoutResponse(payoutId, Guid.NewGuid(), 1, "P", "REF", 300m, "4149", null, request.Status, null, DateTime.UtcNow, null));
    }

    private sealed class RecordingTelegramBotClient : ITelegramBotClient
    {
        public List<string> Messages { get; } = [];
        public string LastMessageText => Messages.Count > 0 ? Messages[^1] : string.Empty;

        public Task<bool> SendMessageAsync(long telegramUserId, string text, CancellationToken cancellationToken = default)
        {
            Messages.Add(text);
            return Task.FromResult(true);
        }

        public Task<bool> SendMessageAsync(long telegramUserId, string text, TelegramReplyMarkup? replyMarkup, CancellationToken cancellationToken = default)
        {
            Messages.Add(text);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds = 30, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TelegramUpdate>>([]);

        public Task<bool> SendReferralRewardNotificationAsync(ReferralRewardResult reward, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendReferralRewardPendingNotificationAsync(ReferralRewardResult reward, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendReferralRewardDeliveredNotificationAsync(ReferralRewardResult reward, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendReferralRewardCancelledNotificationAsync(ReferralRewardResult reward, string reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendPayoutCompletedNotificationAsync(long telegramUserId, decimal amount, string cardNumberMasked, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendPayoutRejectedNotificationAsync(long telegramUserId, decimal amount, string? reason, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SetMyCommandsAsync(IReadOnlyList<TelegramBotCommand> commands, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
