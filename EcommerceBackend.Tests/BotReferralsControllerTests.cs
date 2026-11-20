using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Controllers;
using EcommerceBackend.Domain.Entities;
using EcommerceBackend.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class BotReferralsControllerTests
{
    private const string ValidSecret = "sasha-bot-secret-123";

    [Fact]
    public async Task GetProfile_ReturnsUnauthorized_WhenSecretIsInvalid()
    {
        var fakeReferralService = new FakeReferralService();
        var controller = CreateController(fakeReferralService, ValidSecret);

        var result = await controller.GetProfile(12345L, "wrong-secret", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetProfile_ReturnsUnauthorized_WhenServerSecretIsNotConfigured()
    {
        var fakeReferralService = new FakeReferralService();
        var controller = CreateController(fakeReferralService, string.Empty);

        var result = await controller.GetProfile(12345L, "any-secret", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
    }

    [Fact]
    public async Task GetProfile_ReturnsNotFound_WhenPartnerDoesNotExist()
    {
        var fakeReferralService = new FakeReferralService();
        var controller = CreateController(fakeReferralService, ValidSecret);

        var result = await controller.GetProfile(99999L, ValidSecret, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetProfile_ReturnsOk_WhenPartnerExists()
    {
        var fakeReferralService = new FakeReferralService();
        fakeReferralService.ProfileToReturn = new BotPartnerProfileResponse(
            Guid.NewGuid(),
            12345L,
            "Sasha",
            "SASHA1",
            5.0m,
            250.0m,
            50.0m,
            5,
            3,
            DateTime.UtcNow);

        var controller = CreateController(fakeReferralService, ValidSecret);

        var result = await controller.GetProfile(12345L, ValidSecret, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var profile = Assert.IsType<BotPartnerProfileResponse>(okResult.Value);
        Assert.Equal(12345L, profile.TelegramUserId);
        Assert.Equal(250.0m, profile.AvailableBalance);
        Assert.Equal(50.0m, profile.PendingBalance);
    }

    [Fact]
    public async Task Register_ReturnsCreated_WhenValid()
    {
        var fakeReferralService = new FakeReferralService();
        var controller = CreateController(fakeReferralService, ValidSecret);

        var request = new BotRegisterPartnerRequest
        {
            TelegramUserId = 777L,
            Name = "NewPartner",
            ReferralCode = "NEW777"
        };

        var result = await controller.Register(request, ValidSecret, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var created = Assert.IsType<BotPartnerProfileResponse>(createdResult.Value);
        Assert.Equal(777L, created.TelegramUserId);
        Assert.Equal("NEW777", created.ReferralCode);
    }

    [Fact]
    public async Task GetStats_ReturnsOk_WhenFound()
    {
        var fakeReferralService = new FakeReferralService();
        fakeReferralService.StatsToReturn = new BotPartnerStatsResponse(
            "Sasha",
            "SASHA1",
            5.0m,
            500.0m,
            100.0m,
            12000.0m,
            10,
            8,
            2,
            0);

        var controller = CreateController(fakeReferralService, ValidSecret);

        var result = await controller.GetStats(12345L, ValidSecret, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var stats = Assert.IsType<BotPartnerStatsResponse>(okResult.Value);
        Assert.Equal(500.0m, stats.AvailableBalance);
        Assert.Equal(100.0m, stats.PendingBalance);
        Assert.Equal(12000.0m, stats.TotalTurnover);
    }

    [Fact]
    public async Task GetOrders_ReturnsOrders_WhenFound()
    {
        var fakeReferralService = new FakeReferralService();
        fakeReferralService.OrdersToReturn = new BotPartnerOrdersResponse(
            1,
            new[]
            {
                new BotPartnerOrderDto(
                    101,
                    "ord_101",
                    1000m,
                    50m,
                    OrderStatus.Delivered,
                    PaymentStatus.Paid,
                    ReferralRewardStatus.Paid,
                    DateTime.UtcNow)
            });

        var controller = CreateController(fakeReferralService, ValidSecret);

        var result = await controller.GetOrders(12345L, 20, 0, ValidSecret, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var orders = Assert.IsType<BotPartnerOrdersResponse>(okResult.Value);
        Assert.Equal(1, orders.TotalCount);
        Assert.Single(orders.Items);
    }

    [Fact]
    public async Task RequestPayout_ReturnsOk_WhenValid()
    {
        var fakeReferralService = new FakeReferralService();
        var controller = CreateController(fakeReferralService, ValidSecret);

        var request = new BotPayoutRequestDto
        {
            Amount = 500m,
            CardNumber = "4149499912345678",
            CardHolderName = "Sasha"
        };

        var result = await controller.RequestPayout(12345L, request, ValidSecret, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payout = Assert.IsType<BotPayoutResponse>(okResult.Value);
        Assert.Equal(500m, payout.Amount);
        Assert.Equal(PayoutRequestStatus.Pending, payout.Status);
    }

    [Fact]
    public async Task GetPayouts_ReturnsList()
    {
        var fakeReferralService = new FakeReferralService();
        var controller = CreateController(fakeReferralService, ValidSecret);

        var result = await controller.GetPayouts(12345L, ValidSecret, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payouts = Assert.IsAssignableFrom<IReadOnlyList<BotPayoutResponse>>(okResult.Value);
        Assert.NotNull(payouts);
    }

    private static BotReferralsController CreateController(IReferralService referralService, string secret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotSecret"] = secret
            })
            .Build();

        return new BotReferralsController(referralService, config);
    }

    private sealed class FakeReferralService : IReferralService
    {
        public BotPartnerProfileResponse? ProfileToReturn { get; set; }
        public BotPartnerStatsResponse? StatsToReturn { get; set; }
        public BotPartnerOrdersResponse? OrdersToReturn { get; set; }

        public Task<Partner?> FindPartnerByCodeAsync(string? referralCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<Partner?>(null);

        public Task<bool> AttachReferrerAsync(Order order, string? referralCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<ReferralRewardResult?> ProcessPaidOrderAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferralRewardResult?>(null);

        public Task<ReferralRewardResult?> ProcessDeliveredOrderRewardAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferralRewardResult?>(null);

        public Task<ReferralRewardResult?> ProcessCancelledOrderRewardAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReferralRewardResult?>(null);

        public Task<IReadOnlyList<PartnerResponse>> GetAllPartnersAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PartnerResponse>>(Array.Empty<PartnerResponse>());

        public Task<PartnerResponse?> GetPartnerByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PartnerResponse?>(null);

        public Task<PartnerResponse> CreatePartnerAsync(CreatePartnerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PartnerResponse(Guid.NewGuid(), request.TelegramUserId, request.Name, request.ReferralCode, request.CommissionPercent, 0m, DateTime.UtcNow));

        public Task<PartnerResponse> UpdatePartnerAsync(Guid id, UpdateReferralPartnerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PartnerResponse(id, 12345L, request.Name ?? "Partner", request.ReferralCode ?? "REF", request.CommissionPercent ?? 5m, 0m, DateTime.UtcNow));

        public Task<ReferralCodeValidationResponse> ValidateCodeAsync(string? referralCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReferralCodeValidationResponse(true, referralCode, "Partner"));

        public Task<BotPartnerProfileResponse?> GetBotPartnerProfileAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProfileToReturn);

        public Task<BotPartnerProfileResponse> RegisterBotPartnerAsync(BotRegisterPartnerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BotPartnerProfileResponse(Guid.NewGuid(), request.TelegramUserId, request.Name, request.ReferralCode ?? "AUTO1", 5.0m, 0m, 0m, 0, 0, DateTime.UtcNow));

        public Task<BotPartnerStatsResponse?> GetBotPartnerStatsAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StatsToReturn);

        public Task<BotPartnerOrdersResponse?> GetBotPartnerOrdersAsync(long telegramUserId, int limit = 20, int offset = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult(OrdersToReturn);

        public Task<BotPayoutResponse> RequestPayoutAsync(long telegramUserId, BotPayoutRequestDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BotPayoutResponse(Guid.NewGuid(), request.Amount, "**** 1234", request.CardHolderName, PayoutRequestStatus.Pending, 100m, DateTime.UtcNow));

        public Task<IReadOnlyList<BotPayoutResponse>> GetBotPartnerPayoutsAsync(long telegramUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BotPayoutResponse>>(Array.Empty<BotPayoutResponse>());

        public Task<IReadOnlyList<AdminPayoutResponse>> GetAllPayoutRequestsAsync(PayoutRequestStatus? status = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AdminPayoutResponse>>(Array.Empty<AdminPayoutResponse>());

        public Task<AdminPayoutResponse> ProcessPayoutRequestAsync(Guid payoutId, UpdatePayoutStatusRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminPayoutResponse(payoutId, Guid.NewGuid(), 123L, "Partner", "REF1", 300m, "4149499912345678", null, request.Status, request.AdminComment, DateTime.UtcNow, DateTime.UtcNow));
    }
}
