using System.Text;
using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Controllers;
using EcommerceBackend.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class PaymentsControllerTests
{
    [Fact]
    public async Task HandleWebhook_ReturnsUnauthorized_WhenSecretDoesNotMatch()
    {
        var orderService = new FakeOrderService();
        var monobankService = new FakeMonobankService();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:WebhookSecret"] = "super-secret"
            })
            .Build();
        var controller = new PaymentsController(
            orderService,
            monobankService,
            config,
            NullLogger<PaymentsController>.Instance);

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
        var monobankService = new FakeMonobankService();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:WebhookSecret"] = "super-secret"
            })
            .Build();
        var controller = new PaymentsController(
            orderService,
            monobankService,
            config,
            NullLogger<PaymentsController>.Instance);

        var result = await controller.HandleWebhook(
            new PaymentWebhookRequest { OrderId = Guid.NewGuid(), PaymentStatus = PaymentStatus.Paid },
            "super-secret",
            CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.IsType<OrderPaymentConfirmationResponse>(okResult.Value);
        Assert.True(orderService.WebhookCalled);
    }

    [Fact]
    public async Task HandleMonobankWebhook_ReturnsUnauthorized_WhenSignatureIsInvalid()
    {
        var orderService = new FakeOrderService();
        var monobankService = new FakeMonobankService { VerifySignatureResult = false };
        var config = new ConfigurationBuilder().Build();
        var controller = new PaymentsController(
            orderService,
            monobankService,
            config,
            NullLogger<PaymentsController>.Instance);

        var httpContext = new DefaultHttpContext();
        var json = "{\"invoiceId\":\"inv-1\",\"status\":\"success\",\"amount\":10000}";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        httpContext.Request.Headers["X-Sign"] = "invalid-sig";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.HandleMonobankWebhook(CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
        Assert.False(monobankService.ProcessWebhookCalled);
    }

    [Fact]
    public async Task HandleMonobankWebhook_ReturnsOk_WhenSignatureIsValid()
    {
        var orderService = new FakeOrderService();
        var monobankService = new FakeMonobankService { VerifySignatureResult = true };
        var config = new ConfigurationBuilder().Build();
        var controller = new PaymentsController(
            orderService,
            monobankService,
            config,
            NullLogger<PaymentsController>.Instance);

        var httpContext = new DefaultHttpContext();
        var json = "{\"invoiceId\":\"inv-1\",\"status\":\"success\",\"amount\":10000,\"reference\":\"ref-1\"}";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        httpContext.Request.Headers["X-Sign"] = "valid-sig";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await controller.HandleMonobankWebhook(CancellationToken.None);

        var ok = Assert.IsType<OkResult>(result);
        Assert.Equal(200, ok.StatusCode);
        Assert.True(monobankService.ProcessWebhookCalled);
    }

    private sealed class FakeMonobankService : IMonobankService
    {
        public bool VerifySignatureResult { get; set; } = true;
        public bool ProcessWebhookCalled { get; private set; }

        public Task<MonobankInvoiceResponse> CreateInvoiceForOrderAsync(
            string orderPublicId,
            string? customRedirectUrl = null,
            string? customWebhookUrl = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<MonobankInvoiceResponse> CreateCustomInvoiceAsync(
            CreateCustomMonobankInvoiceRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MonobankInvoiceResponse { InvoiceId = "test_inv", PageUrl = "https://pay.mbank.biz/test" });

        public Task<bool> VerifyWebhookSignatureAsync(
            byte[] rawBodyBytes,
            string? xSignBase64,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(VerifySignatureResult);

        public Task<OrderPaymentConfirmationResponse?> ProcessWebhookAsync(
            MonobankWebhookPayload payload,
            CancellationToken cancellationToken = default)
        {
            ProcessWebhookCalled = true;
            return Task.FromResult<OrderPaymentConfirmationResponse?>(null);
        }

        public Task<MonobankInvoiceStatusResponse> GetInvoiceStatusAsync(
            string invoiceId,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
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
