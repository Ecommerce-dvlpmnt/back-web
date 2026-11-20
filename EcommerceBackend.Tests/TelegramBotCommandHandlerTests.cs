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
    }

    private static TelegramBotCommandHandler CreateHandler(
        FakeReferralService referralService,
        FakeTelegramBotClient telegramClient)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Referral:PublicSiteUrl"] = "https://front-web-pi.vercel.app"
            })
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
        decimal availableBalance = 0m)
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
            DateTime.UtcNow);
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
        public BotPartnerProfileResponse? Profile { get; set; }
        public BotPartnerProfileResponse? RegisteredProfile { get; set; }
        public BotPartnerStatsResponse? Stats { get; set; }
        public BotPartnerOrdersResponse? Orders { get; set; }
        public IReadOnlyList<BotPayoutResponse> Payouts { get; set; } = Array.Empty<BotPayoutResponse>();

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
            Task.FromResult<IReadOnlyList<PartnerResponse>>(Array.Empty<PartnerResponse>());

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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminPayoutResponse(
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
}
