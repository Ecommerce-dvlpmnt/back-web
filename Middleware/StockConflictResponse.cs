using EcommerceBackend.Application.Exceptions;

namespace EcommerceBackend.Middleware;

public sealed record StockConflictResponse(
    int Code,
    string Message,
    IReadOnlyList<InsufficientStockItem> Items,
    string TraceId);
