namespace EcommerceBackend.Application.Exceptions;

public sealed class InsufficientStockException(
    IReadOnlyList<InsufficientStockItem> items)
    : Exception("Одного або кількох товарів недостатньо на складі.")
{
    public IReadOnlyList<InsufficientStockItem> Items { get; } = items;
}

public sealed record InsufficientStockItem(
    Guid ProductId,
    string? ProductName,
    int RequestedQuantity,
    int AvailableQuantity);
