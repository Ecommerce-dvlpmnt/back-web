using EcommerceBackend.Application.Services;
using EcommerceBackend.Domain.Enums;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class OrderCancellationPolicyTests
{
    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    public void Check_AllowsCancellation_ForNewUnpaidOrder(PaymentStatus paymentStatus)
    {
        var decision = OrderCancellationPolicy.Check(OrderStatus.New, paymentStatus);

        Assert.Equal(OrderCancellationDecision.CanCancel, decision);
    }

    [Fact]
    public void Check_RejectsCancellation_WhenNewOrderIsPaid()
    {
        var decision = OrderCancellationPolicy.Check(OrderStatus.New, PaymentStatus.Paid);

        Assert.Equal(OrderCancellationDecision.Paid, decision);
    }

    [Theory]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Packed)]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Refused)]
    [InlineData(OrderStatus.Returned)]
    public void Check_RejectsCancellation_WhenStatusIsNotNew(OrderStatus status)
    {
        var decision = OrderCancellationPolicy.Check(status, PaymentStatus.Pending);

        Assert.Equal(OrderCancellationDecision.NotNewStatus, decision);
    }

    [Fact]
    public void Check_ReportsAlreadyCancelled()
    {
        var decision = OrderCancellationPolicy.Check(OrderStatus.Cancelled, PaymentStatus.Pending);

        Assert.Equal(OrderCancellationDecision.AlreadyCancelled, decision);
    }

    [Fact]
    public void Check_ReportsAlreadyCancelled_BeforeCheckingPayment()
    {
        var decision = OrderCancellationPolicy.Check(OrderStatus.Cancelled, PaymentStatus.Paid);

        Assert.Equal(OrderCancellationDecision.AlreadyCancelled, decision);
    }
}
