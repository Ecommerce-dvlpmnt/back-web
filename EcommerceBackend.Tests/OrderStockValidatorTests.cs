using EcommerceBackend.Application.Services;
using EcommerceBackend.Domain.Entities;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class OrderStockValidatorTests
{
    [Fact]
    public void GetShortages_ReturnsRequestedAndAvailableStockWhenQuantityExceedsStock()
    {
        var productId = Guid.NewGuid();
        var product = new Product
        {
            Id = productId,
            Name = "Desk lamp",
            Stock = 2
        };

        var shortages = OrderStockValidator.GetShortages(
            new Dictionary<Guid, int> { [productId] = 3 },
            new Dictionary<Guid, Product> { [productId] = product });

        var shortage = Assert.Single(shortages);
        Assert.Equal(productId, shortage.ProductId);
        Assert.Equal("Desk lamp", shortage.ProductName);
        Assert.Equal(3, shortage.RequestedQuantity);
        Assert.Equal(2, shortage.AvailableQuantity);
    }

    [Fact]
    public void GetShortages_ReportsMissingProductAsZeroAvailable()
    {
        var productId = Guid.NewGuid();

        var shortage = Assert.Single(OrderStockValidator.GetShortages(
            new Dictionary<Guid, int> { [productId] = 1 },
            new Dictionary<Guid, Product>()));

        Assert.Equal(productId, shortage.ProductId);
        Assert.Null(shortage.ProductName);
        Assert.Equal(1, shortage.RequestedQuantity);
        Assert.Equal(0, shortage.AvailableQuantity);
    }
}
