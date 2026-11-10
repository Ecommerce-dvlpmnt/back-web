namespace EcommerceBackend.Application.Exceptions;

public sealed class InsufficientStockException(
    IReadOnlyList<InsufficientStockItem> items)
    : Exception("One or more products do not have enough stock.")
{
    public IReadOnlyList<InsufficientStockItem> Items { get; } = items;
}

public sealed record InsufficientStockItem(
    Guid ProductId,
    string? ProductName,
    int RequestedQuantity,
    int AvailableQuantity);
