using System.Text.Json;
using EcommerceBackend.Application.Exceptions;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class GlobalExceptionHandlingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsConflictWithStockDetails()
    {
        var productId = Guid.NewGuid();
        var stockException = new InsufficientStockException(
        [
            new InsufficientStockItem(productId, "Desk lamp", 3, 2)
        ]);
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw stockException,
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<StockConflictResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal(StatusCodes.Status409Conflict, response.Code);
        var item = Assert.Single(response.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(3, item.RequestedQuantity);
        Assert.Equal(2, item.AvailableQuantity);
    }
}
