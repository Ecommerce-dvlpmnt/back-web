using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Controllers;
using EcommerceBackend.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class PaymentsControllerTests
{
    [Fact]
    public async Task HandleWebhook_ReturnsUnauthorized_WhenSecretDoesNotMatch()
    {
        var orderService = new FakeOrderService();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:WebhookSecret"] = "super-secret"
            })
            .Build();
        var controller = new PaymentsController(orderService, config);

        var result = await controller.HandleWebhook(
            new PaymentWebhookRequest { OrderId = Guid.NewGuid() },
            "wrong-secret",
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.False(orderService.WebhookCalled);
    }

    [Fact]
    public async Task HandleWebhook_ReturnsOk_WhenSecretMatches()
    {
        var orderService = new FakeOrderService();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:WebhookSecret"] = "super-secret"
            })
            .Build();
        var controller = new PaymentsController(orderService, config);

        var result = await controller.HandleWebhook(
            new PaymentWebhookRequest { OrderId = Guid.NewGuid(), PaymentStatus = PaymentStatus.Paid },
            "super-secret",
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<OrderPaymentConfirmationResponse>(okResult.Value);
        Assert.True(orderService.WebhookCalled);
    }

    private sealed class FakeOrderService : IOrderService
    {
        public bool WebhookCalled { get; private set; }

        public Task<OrderResponse> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<OrderResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<AdminOrderDetailResponse?> GetAdminOrderDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<OrderResponse?> GetByPublicIdAsync(string publicId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<OrderResponse> CancelByPublicIdAsync(string publicId, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<PagedOrderListResponse> GetAllAsync(OrderListQuery query, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<OrderResponse> UpdateStatusAsync(Guid id, UpdateOrderStatusRequest request, Guid? adminId = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<AdminOrderDetailResponse?> UpdateCommentAsync(Guid id, string? comment, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<OrderPaymentConfirmationResponse> ConfirmPaymentAsync(Guid id, ConfirmOrderPaymentRequest? request = null, Guid? adminId = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<OrderPaymentConfirmationResponse> ProcessPaymentWebhookAsync(PaymentWebhookRequest request, CancellationToken cancellationToken = default)
        {
            WebhookCalled = true;
            var order = new OrderResponse(
                Guid.NewGuid(),
                100,
                "abc",
                "Test",
                "User",
                "Test User",
                "+380991112233",
                null,
                null,
                "Київ",
                "Відділення №1",
                OrderStatus.New,
                PaymentMethod.Online,
                PaymentStatus.Paid,
                1000m,
                DateTime.UtcNow,
                Array.Empty<OrderItemResponse>());

            return Task.FromResult(new OrderPaymentConfirmationResponse(order, null, false));
        }
    }
}
