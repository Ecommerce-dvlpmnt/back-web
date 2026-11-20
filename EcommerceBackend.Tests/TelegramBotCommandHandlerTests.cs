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

public sealed class TelegramBotCommandHandlerTests
{
    [Fact]
    public async Task StartCommand_RegistersPartnerAndSendsReferralLink()
    {
        var referralService = new FakeReferralService
        {
            RegisteredProfile = CreateProfile(1001, "SASHA5")
        };
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient);

        await handler.HandleUpdateAsync(CreateUpdate(1001, "/start"));

        Assert.True(referralService.RegisterCalled);
        Assert.Equal(1001, referralService.LastRegisterRequest?.TelegramUserId);
        Assert.Single(telegramClient.Messages);
        Assert.Contains("Вітаю", telegramClient.Messages[0].Text);
        Assert.Contains("https://front-web-pi.vercel.app/?ref=SASHA5", telegramClient.Messages[0].Text);
        Assert.DoesNotContain("/help", telegramClient.Messages[0].Text);
        Assert.True(telegramClient.ReplyMarkups[0]?.IsPersistent);
        Assert.Contains(
            "Статистика",
            telegramClient.ReplyMarkups[0]!.Keyboard.SelectMany(row => row));
    }

    [Fact]
    public async Task StatsCommand_SendsBalances()
    {
        var referralService = new FakeReferralService
        {
            Profile = CreateProfile(1002, "SASHA5"),
            Stats = new BotPartnerStatsResponse(
                "Sasha",
                "SASHA5",
                5m,
                500m,
                100m,
                12000m,
                10,
                8,
                2,
                0)
        };
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient);

        await handler.HandleUpdateAsync(CreateUpdate(1002, "/stats"));

        Assert.Single(telegramClient.Messages);
        Assert.Contains("500.00", telegramClient.Messages[0].Text);
        Assert.Contains("100.00", telegramClient.Messages[0].Text);
        Assert.Contains("12000.00", telegramClient.Messages[0].Text);
    }

    [Fact]
    public async Task WithdrawCommand_BelowMinimum_DoesNotAskForCard()
    {
        var referralService = new FakeReferralService
        {
            Profile = CreateProfile(1003, "SASHA5", availableBalance: 100m)
        };
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient);

        await handler.HandleUpdateAsync(CreateUpdate(1003, "/withdraw"));

        Assert.Single(telegramClient.Messages);
        Assert.Contains("Мінімальна сума", telegramClient.Messages[0].Text);
    }

    [Fact]
    public async Task WithdrawFlow_CreatesPayoutRequestAndSendsConfirmation()
    {
        var referralService = new FakeReferralService
        {
            Profile = CreateProfile(1004, "SASHA5", availableBalance: 500m)
        };
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient);

        await handler.HandleUpdateAsync(CreateUpdate(1004, "/withdraw"));
        await handler.HandleUpdateAsync(CreateUpdate(1004, "500"));
        await handler.HandleUpdateAsync(CreateUpdate(1004, "4149 4999 1234 5678"));

        Assert.NotNull(referralService.LastPayoutRequest);
        Assert.Equal(500m, referralService.LastPayoutRequest!.Amount);
        Assert.Equal("4149499912345678", referralService.LastPayoutRequest.CardNumber);
        Assert.Equal(3, telegramClient.Messages.Count);
        Assert.Contains("Заявку", telegramClient.Messages[^1].Text);
        Assert.False(TelegramBotCommandHandler.HasActiveWithdrawalState(1004));
    }

    [Fact]
    public async Task WithdrawFlow_WithSavedCard_AllowsOneClickConfirmation()
    {
        var referralService = new FakeReferralService
        {
            Profile = CreateProfile(1005, "SASHA5", availableBalance: 600m, cardNumber: "4441111122221234")
        };
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient);

        await handler.HandleUpdateAsync(CreateUpdate(1005, "/withdraw"));
        await handler.HandleUpdateAsync(CreateUpdate(1005, "500"));

        Assert.Contains("<code>4441 •••• •••• 1234</code>", telegramClient.Messages[^1].Text);
        Assert.Equal(TelegramBotMenu.SavedCardConfirmationReplyMarkup, telegramClient.ReplyMarkups[^1]);

        await handler.HandleUpdateAsync(CreateUpdate(1005, TelegramBotMenu.ConfirmSavedCardButton));

        Assert.NotNull(referralService.LastPayoutRequest);
        Assert.Equal(500m, referralService.LastPayoutRequest!.Amount);
        Assert.Equal("4441111122221234", referralService.LastPayoutRequest.CardNumber);
        Assert.Contains("Заявку на виведення 500.00 грн створено", telegramClient.Messages[^1].Text);
        Assert.False(TelegramBotCommandHandler.HasActiveWithdrawalState(1005));
    }

    [Fact]
    public async Task WithdrawFlow_WithSavedCard_AllowsChoosingAnotherCard()
    {
        var referralService = new FakeReferralService
        {
            Profile = CreateProfile(1006, "SASHA5", availableBalance: 600m, cardNumber: "4441111122221234")
        };
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient);

        await handler.HandleUpdateAsync(CreateUpdate(1006, "/withdraw"));
        await handler.HandleUpdateAsync(CreateUpdate(1006, "500"));
        await handler.HandleUpdateAsync(CreateUpdate(1006, TelegramBotMenu.EnterNewCardButton));

        Assert.Contains("Введіть номер банківської картки (16 цифр)", telegramClient.Messages[^1].Text);

        await handler.HandleUpdateAsync(CreateUpdate(1006, "5375 4141 0000 9999"));

        Assert.NotNull(referralService.LastPayoutRequest);
        Assert.Equal("5375414100009999", referralService.LastPayoutRequest!.CardNumber);
        Assert.False(TelegramBotCommandHandler.HasActiveWithdrawalState(1006));
    }

    [Fact]
    public async Task WithdrawFlow_StateIsAutomaticallyCleared_WhenUserSwitchesToMenuButton()
    {
        var referralService = new FakeReferralService
        {
            Profile = CreateProfile(1007, "SASHA5", availableBalance: 600m),
            Stats = new BotPartnerStatsResponse("Sasha", "SASHA5", 5m, 600m, 0m, 12000m, 5, 5, 0, 0)
        };
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient);

        await handler.HandleUpdateAsync(CreateUpdate(1007, TelegramBotMenu.WithdrawButton));
        Assert.True(TelegramBotCommandHandler.HasActiveWithdrawalState(1007));

        // Користувач передумав і натиснув "Статистика"
        await handler.HandleUpdateAsync(CreateUpdate(1007, TelegramBotMenu.StatsButton));
        Assert.False(TelegramBotCommandHandler.HasActiveWithdrawalState(1007));

        // Якщо після цього користувач введе число "500", бот НЕ повинен продовжувати старе виведення
        await handler.HandleUpdateAsync(CreateUpdate(1007, "500"));
        Assert.Null(referralService.LastPayoutRequest);
        Assert.Contains("Скористайтеся меню внизу", telegramClient.Messages[^1].Text);
    }

    [Fact]
    public async Task StartCommand_WrapsCodeAndLinkInHtmlCodeTags_AndEscapesUnsafeCharacters()
    {
        var referralService = new FakeReferralService
        {
            RegisteredProfile = new BotPartnerProfileResponse(
                Guid.NewGuid(),
                1008,
                "Sasha <Admin> & Co",
                "JINGLE5",
                5m,
                0m,
                0m,
                0,
                0,
                DateTime.UtcNow)
        };
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient);

        await handler.HandleUpdateAsync(CreateUpdate(1008, "/start"));

        var text = telegramClient.Messages[0].Text;
        Assert.Contains("<b>Sasha &lt;Admin&gt; &amp; Co</b>", text);
        Assert.Contains("<code>JINGLE5</code>", text);
        Assert.Contains("<code>https://front-web-pi.vercel.app/?ref=JINGLE5</code>", text);
    }

    [Fact]
    public async Task AdminStartCommand_ShowsAdminMenu_AndDoesNotRegisterAsSchooler()
    {
        var referralService = new FakeReferralService();
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient, adminIds: "777001, 777002");

        await handler.HandleUpdateAsync(CreateUpdate(777001, "/start"));

        Assert.False(referralService.RegisterCalled);
        Assert.Single(telegramClient.Messages);
        Assert.Contains("Адмін-панель Jingle Decor", telegramClient.Messages[0].Text);
        Assert.Equal(TelegramBotMenu.AdminReplyMarkup, telegramClient.ReplyMarkups[0]);
    }

    [Fact]
    public async Task AdminOrdersButton_DisplaysRecentOrdersWithItemsAndPromoOrReferral()
    {
        var referralService = new FakeReferralService
        {
            AdminOrders =
            [
                new AdminBotOrderNotificationDto(
                    Guid.NewGuid(),
                    105,
                    "ord_105",
                    "Іван Іванов",
                    "+380501112233",
                    "Київ",
                    "Відділення №15",
                    PaymentMethod.CashOnDelivery,
                    OrderStatus.New,
                    1700m,
                    300m,
                    "FAMILY15",
                    15m,
                    null,
                    null,
                    0m,
                    DateTime.UtcNow,
                    [
                        new AdminBotOrderItemDto("Гірлянда штора 3x3", 2, 1000m, 2000m)
                    ])
            ]
        };
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient, adminIds: "777001");

        await handler.HandleUpdateAsync(CreateUpdate(777001, TelegramBotMenu.AdminOrdersButton));

        Assert.Single(telegramClient.Messages);
        var text = telegramClient.Messages[0].Text;
        Assert.Contains("#105", text);
        Assert.Contains("Гірлянда штора 3x3 × 2 шт.", text);
        Assert.Contains("<code>FAMILY15</code>", text);
        Assert.Contains("1700.00 грн", text);
    }

    [Fact]
    public async Task AdminReferralsButton_DisplaysWhoInvitedHowManyAndTurnover()
    {
        var referralService = new FakeReferralService
        {
            AllPartners =
            [
                new PartnerResponse(
                    Guid.NewGuid(),
                    555001,
                    "Школяр Максим",
                    "MAX5",
                    5m,
                    450m,
                    DateTime.UtcNow,
                    PendingBalance: 150m,
                    AvailableBalance: 450m,
                    TotalOrders: 12,
                    DeliveredOrders: 9,
                    TotalTurnover: 12000m,
                    CardNumber: "4149499912345678")
            ]
        };
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient, adminIds: "777001");

        await handler.HandleUpdateAsync(CreateUpdate(777001, TelegramBotMenu.AdminReferralsButton));

        Assert.Single(telegramClient.Messages);
        var text = telegramClient.Messages[0].Text;
        Assert.Contains("Школяр Максим", text);
        Assert.Contains("<code>MAX5</code>", text);
        Assert.Contains("Запрошено замовлень: <b>12</b> (доставлено: <b>9</b>)", text);
        Assert.Contains("12000.00 грн", text);
        Assert.Contains("<code>4149499912345678</code>", text);
    }

    [Fact]
    public async Task AdminCreatePromoWizard_CreatesPromoCodeWithDiscountAndLimit()
    {
        var referralService = new FakeReferralService();
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient, adminIds: "777001");

        await handler.HandleUpdateAsync(CreateUpdate(777001, TelegramBotMenu.AdminCreatePromoButton));
        Assert.True(TelegramBotCommandHandler.HasActiveAdminPromoState(777001));

        await handler.HandleUpdateAsync(CreateUpdate(777001, "FAMILY15"));
        await handler.HandleUpdateAsync(CreateUpdate(777001, "15"));
        await handler.HandleUpdateAsync(CreateUpdate(777001, "5"));

        Assert.False(TelegramBotCommandHandler.HasActiveAdminPromoState(777001));
        Assert.NotNull(referralService.LastCreatedPromoRequest);
        Assert.Equal("FAMILY15", referralService.LastCreatedPromoRequest!.Code);
        Assert.Equal(15m, referralService.LastCreatedPromoRequest.DiscountPercent);
        Assert.Equal(5, referralService.LastCreatedPromoRequest.MaxUses);
        Assert.Contains("Промокод успішно створено", telegramClient.Messages[^1].Text);
        Assert.Contains("<code>FAMILY15</code>", telegramClient.Messages[^1].Text);
    }

    [Fact]
    public async Task AdminCallbackQuery_ApprovesPayout_AndRejectsNonAdmin()
    {
        var referralService = new FakeReferralService();
        var telegramClient = new FakeTelegramBotClient();
        var handler = CreateHandler(referralService, telegramClient, adminIds: "777001");
        var payoutId = Guid.NewGuid();

        // Спроба звичайного школяра натиснути callback кнопку адміна
        var schoolerCallback = new TelegramUpdate(
            1,
             null,
            new TelegramCallbackQuery(
                "cb_1",
                new TelegramUser(999999, false, "Hacker", null, "hacker", null),
                null,
                $"payout_ok:{payoutId:N}"));

        await handler.HandleUpdateAsync(schoolerCallback);
        Assert.Null(referralService.LastProcessedPayoutId);

        // Адмін натискає кнопку підтвердження виплати
        var adminCallback = new TelegramUpdate(
            2,
            null,
            new TelegramCallbackQuery(
                "cb_2",
                new TelegramUser(777001, false, "Admin", null, "admin", null),
                null,
                $"payout_ok:{payoutId:N}"));

        await handler.HandleUpdateAsync(adminCallback);
        Assert.Equal(payoutId, referralService.LastProcessedPayoutId);
        Assert.Equal(PayoutRequestStatus.Completed, referralService.LastProcessedPayoutRequest?.Status);
        Assert.Contains("ВИПЛАЧЕНО", telegramClient.Messages[^1].Text);
    }

    private static TelegramBotCommandHandler CreateHandler(
        FakeReferralService referralService,
        FakeTelegramBotClient telegramClient,
        string? adminIds = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Referral:PublicSiteUrl"] = "https://front-web-pi.vercel.app"
        };

        if (!string.IsNullOrWhiteSpace(adminIds))
        {
            settings["Telegram:AdminUserIds"] = adminIds;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new TelegramBotCommandHandler(
            referralService,
            telegramClient,
            configuration,
            NullLogger<TelegramBotCommandHandler>.Instance);
    }

    private static BotPartnerProfileResponse CreateProfile(
        long telegramUserId,
        string referralCode,
        decimal availableBalance = 0m,
        string? cardNumber = null)
    {
        return new BotPartnerProfileResponse(
            Guid.NewGuid(),
            telegramUserId,
            "Sasha",
            referralCode,
            5m,
            availableBalance,
            0m,
            0,
            0,
            DateTime.UtcNow,
            cardNumber);
    }

    private static TelegramUpdate CreateUpdate(long telegramUserId, string text)
    {
        return new TelegramUpdate(
            1,
            new TelegramMessage(
                1,
                new TelegramUser(telegramUserId, false, "Sasha", null, "sasha", null),
                new TelegramChat(telegramUserId, "private"),
                0,
                text));
    }

    private sealed class FakeTelegramBotClient : ITelegramBotClient
    {
        public List<(long ChatId, string Text)> Messages { get; } = [];
        public List<TelegramReplyMarkup?> ReplyMarkups { get; } = [];

        public Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
            long offset,
            int timeoutSeconds = 30,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TelegramUpdate>>(Array.Empty<TelegramUpdate>());

        public Task<bool> SendMessageAsync(
            long telegramUserId,
            string text,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((telegramUserId, text));
            ReplyMarkups.Add(null);
            return Task.FromResult(true);
        }

        public Task<bool> SendMessageAsync(
            long telegramUserId,
            string text,
            TelegramReplyMarkup? replyMarkup,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((telegramUserId, text));
            ReplyMarkups.Add(replyMarkup);
            return Task.FromResult(true);
        }

        public Task<bool> SetMyCommandsAsync(
            IReadOnlyList<TelegramBotCommand> commands,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendReferralRewardNotificationAsync(
            ReferralRewardResult reward,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendReferralRewardPendingNotificationAsync(
            ReferralRewardResult reward,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendReferralRewardDeliveredNotificationAsync(
            ReferralRewardResult reward,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendReferralRewardCancelledNotificationAsync(
            ReferralRewardResult reward,
            string reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendPayoutCompletedNotificationAsync(
            long telegramUserId,
            decimal amount,
            string cardNumberMasked,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> SendPayoutRejectedNotificationAsync(
            long telegramUserId,
            decimal amount,
            string? reason,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class FakeReferralService : IReferralService
    {
        public bool RegisterCalled { get; private set; }
        public BotRegisterPartnerRequest? LastRegisterRequest { get; private set; }
        public BotPayoutRequestDto? LastPayoutRequest { get; private set; }
        public CreatePromoCodeRequest? LastCreatedPromoRequest { get; private set; }
        public Guid? LastProcessedPayoutId { get; private set; }
        public UpdatePayoutStatusRequest? LastProcessedPayoutRequest { get; private set; }
        public BotPartnerProfileResponse? Profile { get; set; }
        public BotPartnerProfileResponse? RegisteredProfile { get; set; }
        public BotPartnerStatsResponse? Stats { get; set; }
        public BotPartnerOrdersResponse? Orders { get; set; }
        public IReadOnlyList<BotPayoutResponse> Payouts { get; set; } = Array.Empty<BotPayoutResponse>();
        public IReadOnlyList<PartnerResponse> AllPartners { get; set; } = Array.Empty<PartnerResponse>();
        public IReadOnlyList<AdminBotOrderNotificationDto> AdminOrders { get; set; } = Array.Empty<AdminBotOrderNotificationDto>();
        public IReadOnlyList<PromoCodeResponse> PromoCodes { get; set; } = Array.Empty<PromoCodeResponse>();

        public Task<Partner?> FindPartnerByCodeAsync(
            string? referralCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Partner?>(null);

        public Task<bool> AttachReferrerAsync(
            Order order,
            string? referralCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<ReferralRewardResult?> ProcessPaidOrderAsync(
            Guid orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferralRewardResult?>(null);

        public Task<ReferralRewardResult?> ProcessDeliveredOrderRewardAsync(
            Guid orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferralRewardResult?>(null);

        public Task<ReferralRewardResult?> ProcessCancelledOrderRewardAsync(
            Guid orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferralRewardResult?>(null);

        public Task<IReadOnlyList<PartnerResponse>> GetAllPartnersAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AllPartners);

        public Task<PartnerResponse?> GetPartnerByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PartnerResponse?>(null);

        public Task<PartnerResponse> CreatePartnerAsync(
            CreatePartnerRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PartnerResponse(
                Guid.NewGuid(),
                request.TelegramUserId,
                request.Name,
                request.ReferralCode,
                request.CommissionPercent,
                0m,
                DateTime.UtcNow));

        public Task<PartnerResponse> UpdatePartnerAsync(
            Guid id,
            UpdateReferralPartnerRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PartnerResponse(
                id,
                1001L,
                request.Name ?? "Partner",
                request.ReferralCode ?? "REF",
                request.CommissionPercent ?? 5m,
                0m,
                DateTime.UtcNow));

        public Task<ReferralCodeValidationResponse> ValidateCodeAsync(
            string? referralCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReferralCodeValidationResponse(false, null, null));

        public Task<BotPartnerProfileResponse?> GetBotPartnerProfileAsync(
            long telegramUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Profile);

        public Task<BotPartnerProfileResponse> RegisterBotPartnerAsync(
            BotRegisterPartnerRequest request,
            CancellationToken cancellationToken = default)
        {
            RegisterCalled = true;
            LastRegisterRequest = request;
            return Task.FromResult(
                RegisteredProfile ?? new BotPartnerProfileResponse(
                    Guid.NewGuid(),
                    request.TelegramUserId,
                    request.Name,
                    request.ReferralCode ?? "AUTO1",
                    5m,
                    0m,
                    0m,
                    0,
                    0,
                    DateTime.UtcNow));
        }

        public Task<BotPartnerStatsResponse?> GetBotPartnerStatsAsync(
            long telegramUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Stats);

        public Task<BotPartnerOrdersResponse?> GetBotPartnerOrdersAsync(
            long telegramUserId,
            int limit = 20,
            int offset = 0,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders);

        public Task<BotPayoutResponse> RequestPayoutAsync(
            long telegramUserId,
            BotPayoutRequestDto request,
            CancellationToken cancellationToken = default)
        {
            LastPayoutRequest = request;
            return Task.FromResult(new BotPayoutResponse(
                Guid.NewGuid(),
                request.Amount,
                "4149 **** **** 5678",
                request.CardHolderName,
                PayoutRequestStatus.Pending,
                0m,
                DateTime.UtcNow));
        }

        public Task<IReadOnlyList<BotPayoutResponse>> GetBotPartnerPayoutsAsync(
            long telegramUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Payouts);

        public Task<IReadOnlyList<AdminPayoutResponse>> GetAllPayoutRequestsAsync(
            PayoutRequestStatus? status = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdminPayoutResponse>>(Array.Empty<AdminPayoutResponse>());

        public Task<AdminPayoutResponse> ProcessPayoutRequestAsync(
            Guid payoutId,
            UpdatePayoutStatusRequest request,
            CancellationToken cancellationToken = default)
        {
            LastProcessedPayoutId = payoutId;
            LastProcessedPayoutRequest = request;
            return Task.FromResult(new AdminPayoutResponse(
                payoutId,
                Guid.NewGuid(),
                1,
                "Partner",
                "REF1",
                300m,
                "4149499912345678",
                null,
                request.Status,
                request.AdminComment,
                DateTime.UtcNow,
                DateTime.UtcNow));
        }

        public Task<PromoCodeResponse> CreatePromoCodeAsync(
            CreatePromoCodeRequest request,
            CancellationToken cancellationToken = default)
        {
            LastCreatedPromoRequest = request;
            return Task.FromResult(new PromoCodeResponse(
                Guid.NewGuid(),
                request.Code ?? "PROMO_AUTO",
                request.DiscountPercent,
                request.MaxUses,
                0,
                true,
                request.CreatedByTelegramUserId,
                DateTime.UtcNow));
        }

        public Task<IReadOnlyList<PromoCodeResponse>> GetAllPromoCodesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PromoCodes);

        public Task<IReadOnlyList<AdminBotOrderNotificationDto>> GetRecentOrdersForAdminBotAsync(
            int limit = 10,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AdminOrders);
    }
}
