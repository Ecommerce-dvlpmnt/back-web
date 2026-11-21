using System.ComponentModel.DataAnnotations;
using EcommerceBackend.Application.DTOs.Orders;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class OrderRequestValidationTests
{
    [Fact]
    public void Validate_AcceptsNameWithAnyCharacters()
    {
        var request = CreateValidRequest() with { CustomerFirstName = "12" };

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_RequiresFirstName()
    {
        var request = CreateValidRequest() with { CustomerFirstName = null };

        var errors = Validate(request);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(CreateOrderRequest.CustomerFirstName)));
    }

    [Fact]
    public void Validate_RequiresFirstNameOfAtLeastTwoCharacters()
    {
        var request = CreateValidRequest() with { CustomerFirstName = "A" };

        var errors = Validate(request);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(CreateOrderRequest.CustomerFirstName)));
    }

    [Fact]
    public void Validate_RequiresDeliveryCityName()
    {
        var request = CreateValidRequest() with { DeliveryCityName = null };

        var errors = Validate(request);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(CreateOrderRequest.DeliveryCityName)));
    }

    [Fact]
    public void Validate_RequiresDeliveryWarehouseName()
    {
        var request = CreateValidRequest() with { DeliveryWarehouseName = null };

        var errors = Validate(request);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(CreateOrderRequest.DeliveryWarehouseName)));
    }

    private static CreateOrderRequest CreateValidRequest() => new()
    {
        CustomerFirstName = "Олена",
        CustomerLastName = "Коваль",
        CustomerPhone = "+380991234567",
        DeliveryCityName = "Київ",
        DeliveryWarehouseName = "Відділення №12",
        Items =
        [
            new CreateOrderItemRequest
            {
                ProductId = Guid.NewGuid(),
                Quantity = 1
            }
        ]
    };

    private static List<ValidationResult> Validate(CreateOrderRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);
        return results;
    }
}
