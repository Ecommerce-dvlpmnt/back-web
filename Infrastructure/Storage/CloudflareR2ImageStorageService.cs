using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace EcommerceBackend.Infrastructure.Storage;

/// <summary>
/// Завантажує зображення безпосередньо у сумісне з S3 сховище Cloudflare R2 за протоколом AWS Signature V4
/// та повертає публічний HTTPS-URL файлу.
/// </summary>
public sealed class CloudflareR2ImageStorageService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<CloudflareR2ImageStorageService> logger) : IImageStorageService
{
    public const string HttpClientName = "CloudflareR2Storage";

    private const long MaxFileSize = 5 * 1024 * 1024;
    private const string Region = "auto";
    private const string Service = "s3";

    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };

    public static bool IsConfigured(IConfiguration configuration)
    {
        var settings = ReadSettings(configuration);
        return settings is not null;
    }

    public async Task<string> UploadImageAsync(
        IFormFile file,
        string folderName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Length is <= 0 or > MaxFileSize)
            throw new ArgumentException("Розмір зображення повинен бути від 1 байта до 5 МБ.", nameof(file));

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException("Дозволені лише зображення форматів .jpg, .jpeg, .png та .webp.", nameof(file));

        if (string.IsNullOrWhiteSpace(folderName) ||
            folderName is "." or ".." ||
            folderName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("Назва папки повинна бути безпечним ім'ям шляху.", nameof(folderName));
        }

        var settings = ReadSettings(configuration)
            ?? throw new InvalidOperationException(
                "Cloudflare R2 не налаштовано. Задайте секцію 'CloudflareR2' (AccountId, BucketName, AccessKeyId, SecretAccessKey, PublicBaseUrl).");

        await using var input = file.OpenReadStream();
        using var memoryStream = new MemoryStream((int)file.Length);
        await input.CopyToAsync(memoryStream, cancellationToken);
        var bodyBytes = memoryStream.ToArray();

        if (!HasValidSignature(bodyBytes.AsSpan(0, Math.Min(12, bodyBytes.Length)), extension))
            throw new ArgumentException("Вміст файлу не відповідає розширенню зображення.", nameof(file));

        var normalizedExt = extension.ToLowerInvariant();
        var contentType = GetContentType(normalizedExt);
        var objectKey = $"{folderName}/{Guid.NewGuid():N}{normalizedExt}";

        var endpointUri = new Uri(settings.EndpointOrigin);
        var host = endpointUri.IsDefaultPort
            ? endpointUri.Host
            : $"{endpointUri.Host}:{endpointUri.Port}";

        var canonicalUri = $"/{settings.BucketName}/{objectKey}";
        var requestUri = $"{settings.EndpointOrigin}{canonicalUri}";

        var utcNow = DateTime.UtcNow;
        var amzDate = utcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var payloadHash = ComputeSha256Hex(bodyBytes);
        var canonicalHeaders =
            $"content-type:{contentType}\n" +
            $"host:{host}\n" +
            $"x-amz-content-sha256:{payloadHash}\n" +
            $"x-amz-date:{amzDate}\n";

        const string signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = $"PUT\n{canonicalUri}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";

        var credentialScope = $"{dateStamp}/{Region}/{Service}/aws4_request";
        var stringToSign =
            $"AWS4-HMAC-SHA256\n{amzDate}\n{credentialScope}\n{ComputeSha256Hex(Encoding.UTF8.GetBytes(canonicalRequest))}";

        var signingKey = DeriveSigningKey(settings.SecretAccessKey, dateStamp, Region, Service);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(stringToSign)));
        var authorizationHeader =
            $"AWS4-HMAC-SHA256 Credential={settings.AccessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";

        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

        var content = new ByteArrayContent(bodyBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Content = content;

        var httpClient = httpClientFactory.CreateClient(HttpClientName);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Cloudflare R2 upload failed for key '{ObjectKey}'. Status: {StatusCode}, Body: {ErrorBody}",
                objectKey,
                (int)response.StatusCode,
                errorBody);
            throw new HttpRequestException($"Не вдалося завантажити зображення у Cloudflare R2 (HTTP {(int)response.StatusCode}).");
        }

        var publicUrl = $"{settings.PublicBaseUrl}/{objectKey}";
        logger.LogInformation("Uploaded image to Cloudflare R2: {PublicUrl} ({Bytes} bytes)", publicUrl, bodyBytes.Length);
        return publicUrl;
    }

    private static R2Settings? ReadSettings(IConfiguration configuration)
    {
        var accountId = FirstNonPlaceholder(
            configuration["CloudflareR2:AccountId"],
            configuration["R2_ACCOUNT_ID"]);

        var bucketName = FirstNonPlaceholder(
            configuration["CloudflareR2:BucketName"],
            configuration["R2_BUCKET_NAME"]);

        var accessKeyId = FirstNonPlaceholder(
            configuration["CloudflareR2:AccessKeyId"],
            configuration["R2_ACCESS_KEY_ID"]);

        var secretAccessKey = FirstNonPlaceholder(
            configuration["CloudflareR2:SecretAccessKey"],
            configuration["R2_SECRET_ACCESS_KEY"]);

        var publicBaseUrl = FirstNonPlaceholder(
            configuration["CloudflareR2:PublicBaseUrl"],
            configuration["R2_PUBLIC_BASE_URL"]);

        var explicitEndpoint = FirstNonPlaceholder(
            configuration["CloudflareR2:EndpointUrl"],
            configuration["R2_ENDPOINT_URL"]);

        var endpointOrigin = !string.IsNullOrWhiteSpace(explicitEndpoint)
            ? explicitEndpoint.Trim().TrimEnd('/')
            : (!string.IsNullOrWhiteSpace(accountId)
                ? $"https://{accountId.Trim()}.r2.cloudflarestorage.com"
                : null);

        if (string.IsNullOrWhiteSpace(endpointOrigin) ||
            string.IsNullOrWhiteSpace(bucketName) ||
            string.IsNullOrWhiteSpace(accessKeyId) ||
            string.IsNullOrWhiteSpace(secretAccessKey) ||
            string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(endpointOrigin, UriKind.Absolute, out var endpointUri))
            return null;

        // Якщо у EndpointUrl випадково передали шлях з ім'ям бакета (...r2.cloudflarestorage.com/jingle-products), беремо лише origin
        var cleanOrigin = endpointUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');

        return new R2Settings(
            cleanOrigin,
            bucketName.Trim().Trim('/'),
            accessKeyId.Trim(),
            secretAccessKey.Trim(),
            publicBaseUrl.Trim().TrimEnd('/'));
    }

    private static string? FirstNonPlaceholder(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            var trimmed = candidate.Trim();
            if (trimmed.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return trimmed;
        }

        return null;
    }

    private static string GetContentType(string extension) =>
        extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

    private static string ComputeSha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexStringLower(SHA256.HashData(data));

    private static byte[] DeriveSigningKey(string secretKey, string dateStamp, string region, string service)
    {
        var kSecret = Encoding.UTF8.GetBytes($"AWS4{secretKey}");
        var kDate = HMACSHA256.HashData(kSecret, Encoding.UTF8.GetBytes(dateStamp));
        var kRegion = HMACSHA256.HashData(kDate, Encoding.UTF8.GetBytes(region));
        var kService = HMACSHA256.HashData(kRegion, Encoding.UTF8.GetBytes(service));
        return HMACSHA256.HashData(kService, "aws4_request"u8);
    }

    private static bool HasValidSignature(ReadOnlySpan<byte> header, string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" =>
                header.Length >= 3 &&
                header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" =>
                header.Length >= 8 &&
                header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".webp" =>
                header.Length >= 12 &&
                header[..4].SequenceEqual("RIFF"u8) &&
                header[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };

    private sealed record R2Settings(
        string EndpointOrigin,
        string BucketName,
        string AccessKeyId,
        string SecretAccessKey,
        string PublicBaseUrl);
}
