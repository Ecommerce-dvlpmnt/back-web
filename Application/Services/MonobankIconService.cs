using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace EcommerceBackend.Application.Services;

public sealed class MonobankIconService(
    AppDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    ILogger<MonobankIconService> logger) : IMonobankIconService
{
    public const string HttpClientName = "MonobankIcon";
    public const int IconSizePx = 256;
    private const long MaxSourceBytes = 15 * 1024 * 1024;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    public async Task<MonobankSquareIconResult> GetSquareProductIconAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var mainImageUrl = await dbContext.Products
            .AsNoTracking()
            .Where(p => p.Id == productId && p.IsActive)
            .Select(p => p.MainImageUrl)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(mainImageUrl))
            return new MonobankSquareIconResult(null, null);

        var trimmedUrl = mainImageUrl.Trim();
        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var sourceUri)
            || (sourceUri.Scheme != Uri.UriSchemeHttps && sourceUri.Scheme != Uri.UriSchemeHttp))
        {
            return new MonobankSquareIconResult(null, null);
        }

        var cacheKey = $"monobank:square-icon:v1:{productId:N}:{trimmedUrl}";
        if (memoryCache.TryGetValue(cacheKey, out byte[]? cachedBytes) && cachedBytes is { Length: > 0 })
        {
            return new MonobankSquareIconResult(cachedBytes, trimmedUrl);
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Не вдалося завантажити фото товару {ProductId} для іконки Monobank (HTTP {StatusCode}).",
                    productId,
                    (int)response.StatusCode);
                return new MonobankSquareIconResult(null, trimmedUrl);
            }

            if (response.Content.Headers.ContentLength > MaxSourceBytes)
            {
                logger.LogWarning("Фото товару {ProductId} перевищує ліміт {MaxBytes} байт.", productId, MaxSourceBytes);
                return new MonobankSquareIconResult(null, trimmedUrl);
            }

            await using var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var squareJpegBytes = await CropToSquareJpegAsync(sourceStream, IconSizePx, cancellationToken);

            memoryCache.Set(cacheKey, squareJpegBytes, CacheDuration);
            return new MonobankSquareIconResult(squareJpegBytes, trimmedUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Помилка формування квадратної іконки Monobank для товару {ProductId}.", productId);
            return new MonobankSquareIconResult(null, trimmedUrl);
        }
    }

    public static async Task<byte[]> CropToSquareJpegAsync(
        Stream inputStream,
        int size = IconSizePx,
        CancellationToken cancellationToken = default)
    {
        using var image = await Image.LoadAsync(inputStream, cancellationToken);
        image.Mutate(ctx => ctx
            .AutoOrient()
            .Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            })
            .BackgroundColor(Color.White));

        await using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = 86 }, cancellationToken);
        return output.ToArray();
    }
}
