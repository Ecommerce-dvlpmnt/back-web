using EcommerceBackend.Application.Services;
using EcommerceBackend.Domain.Enums;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class OrderStockTransitionPolicyTests
{
    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Packed, OrderStatus.Refused)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Returned)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Returned)]
    public void RequiresStockReturn_WhenActiveOrderBecomesStockReleased(
        OrderStatus oldStatus,
        OrderStatus newStatus)
    {
        Assert.True(OrderStockTransitionPolicy.RequiresStockReturn(oldStatus, newStatus));
        Assert.False(OrderStockTransitionPolicy.RequiresStockDeduction(oldStatus, newStatus));
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled, OrderStatus.New)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Packed)]
    [InlineData(OrderStatus.Refused, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Returned, OrderStatus.Shipped)]
    public void RequiresStockDeduction_WhenStockReleasedOrderIsReactivated(
        OrderStatus oldStatus,
        OrderStatus newStatus)
    {
        Assert.True(OrderStockTransitionPolicy.RequiresStockDeduction(oldStatus, newStatus));
        Assert.False(OrderStockTransitionPolicy.RequiresStockReturn(oldStatus, newStatus));
    }

    [Theory]
    [InlineData(OrderStatus.New, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Packed)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Returned)]
    [InlineData(OrderStatus.Returned, OrderStatus.Cancelled)]
    public void RequiresNoStockChange_WhenBothStatusesHaveSameStockState(
        OrderStatus oldStatus,
        OrderStatus newStatus)
    {
        Assert.False(OrderStockTransitionPolicy.RequiresStockReturn(oldStatus, newStatus));
        Assert.False(OrderStockTransitionPolicy.RequiresStockDeduction(oldStatus, newStatus));
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Refused, true)]
    [InlineData(OrderStatus.Returned, true)]
    [InlineData(OrderStatus.New, false)]
    [InlineData(OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Packed, false)]
    [InlineData(OrderStatus.Shipped, false)]
    [InlineData(OrderStatus.Delivered, false)]
    public void IsStockReleased_ReturnsExpectedState(OrderStatus status, bool expected)
    {
        Assert.Equal(expected, OrderStockTransitionPolicy.IsStockReleased(status));
    }
}
