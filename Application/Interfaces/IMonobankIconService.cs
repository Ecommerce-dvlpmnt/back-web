namespace EcommerceBackend.Application.Interfaces;

public sealed record MonobankSquareIconResult(byte[]? ImageBytes, string? FallbackRedirectUrl);

public interface IMonobankIconService
{
    Task<MonobankSquareIconResult> GetSquareProductIconAsync(
        Guid productId,
        CancellationToken cancellationToken = default);
}
