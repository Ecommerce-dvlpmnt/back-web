using EcommerceBackend.Application.Exceptions;
using EcommerceBackend.Domain.Entities;

namespace EcommerceBackend.Application.Services;

public static class OrderStockValidator
{
    public static IReadOnlyList<InsufficientStockItem> GetShortages(
        IReadOnlyDictionary<Guid, int> requestedQuantities,
        IReadOnlyDictionary<Guid, Product> products) =>
        requestedQuantities
            .Where(item =>
                !products.TryGetValue(item.Key, out var product) ||
                product.Stock < item.Value)
            .Select(item =>
            {
                products.TryGetValue(item.Key, out var product);
                return new InsufficientStockItem(
                    item.Key,
                    product?.Name,
                    item.Value,
                    product?.Stock ?? 0);
            })
            .ToArray();
}
