using System.Net;
using EcommerceBackend.Application.DTOs.Orders;
using EcommerceBackend.Application.Security;
using EcommerceBackend.Domain.Enums;
using EcommerceBackend.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class PreLaunchFixesTests
{
    [Fact]
    public void DatabaseConnectionHelper_DisablesGssEncryption_ForPostgresUri()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATABASE_URL"] = "postgres://user:pass@aws-1-eu-central-1.pooler.supabase.com:5432/postgres?sslmode=require"
            })
            .Build();

        var resolved = DatabaseConnectionHelper.ResolveConnectionString(config);
        var parsed = new NpgsqlConnectionStringBuilder(resolved);

        Assert.Equal(GssEncryptionMode.Disable, parsed.GssEncryptionMode);
        Assert.Equal(SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void DatabaseConnectionHelper_DisablesGssEncryption_ForKeyValueConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=aws-1-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres;Password=secret;SSL Mode=Require"
            })
            .Build();

        var resolved = DatabaseConnectionHelper.ResolveConnectionString(config);
        var parsed = new NpgsqlConnectionStringBuilder(resolved);

        Assert.Equal(GssEncryptionMode.Disable, parsed.GssEncryptionMode);
    }

    [Fact]
    public void CloudflareR2ImageStorageService_IsConfigured_RejectsPlaceholdersAndAcceptsValidConfig()
    {
        var unconfigured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudflareR2:AccountId"] = "YOUR_CLOUDFLARE_ACCOUNT_ID",
                ["CloudflareR2:BucketName"] = "jingle-products",
                ["CloudflareR2:AccessKeyId"] = "YOUR_R2_ACCESS_KEY_ID",
                ["CloudflareR2:SecretAccessKey"] = "YOUR_R2_SECRET_ACCESS_KEY",
                ["CloudflareR2:PublicBaseUrl"] = "https://pub-example.r2.dev"
            })
            .Build();

        Assert.False(CloudflareR2ImageStorageService.IsConfigured(unconfigured));

        var configured = BuildR2Config();
        Assert.True(CloudflareR2ImageStorageService.IsConfigured(configured));
    }

    [Fact]
    public async Task CloudflareR2ImageStorageService_SignsSigV4PutRequest_AndReturnsPublicUrl()
    {
        HttpRequestMessage? capturedRequest = null;
        byte[]? capturedBody = null;

        var handler = new StubHttpMessageHandler(async (req, ct) =>
        {
            capturedRequest = req;
            capturedBody = req.Content is not null ? await req.Content.ReadAsByteArrayAsync(ct) : null;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var factory = new SingleClientHttpClientFactory(new HttpClient(handler));
        var service = new CloudflareR2ImageStorageService(
            factory,
            BuildR2Config(),
            NullLogger<CloudflareR2ImageStorageService>.Instance);

        // Minimal valid JPEG header (FF D8 FF E0 ...)
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
        await using var stream = new MemoryStream(jpegBytes);
        var formFile = new FormFile(stream, 0, jpegBytes.Length, "file", "tree.jpg");

        var url = await service.UploadImageAsync(formFile, "products", CancellationToken.None);

        Assert.StartsWith("https://pub-579e9008393e47219443a996a9a7c1d9.r2.dev/products/", url);
        Assert.EndsWith(".jpg", url);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Put, capturedRequest!.Method);
        Assert.StartsWith("https://acc123.r2.cloudflarestorage.com/jingle-products/products/", capturedRequest.RequestUri!.ToString());
        Assert.True(capturedRequest.Headers.TryGetValues("Authorization", out var authValues));
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=ak_test/", authValues!.Single());
        Assert.Equal(jpegBytes, capturedBody);
    }

    [Fact]
    public async Task CloudflareR2ImageStorageService_RejectsSpoofedExtension()
    {
        var factory = new SingleClientHttpClientFactory(new HttpClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
        var service = new CloudflareR2ImageStorageService(
            factory,
            BuildR2Config(),
            NullLogger<CloudflareR2ImageStorageService>.Instance);

        var notAnImage = "<html>not a jpeg</html>"u8.ToArray();
        await using var stream = new MemoryStream(notAnImage);
        var formFile = new FormFile(stream, 0, notAnImage.Length, "file", "spoof.jpg");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadImageAsync(formFile, "products", CancellationToken.None));
    }

    [Fact]
    public void PaymentWebhookRequest_CancelOrderOnFailure_DefaultsToFalse_AndSupportsExpiredFlag()
    {
        var defaultReq = new PaymentWebhookRequest { PaymentStatus = PaymentStatus.Failed };
        Assert.False(defaultReq.CancelOrderOnFailure);

        var expiredReq = new PaymentWebhookRequest
        {
            PaymentStatus = PaymentStatus.Failed,
            CancelOrderOnFailure = true
        };
        Assert.True(expiredReq.CancelOrderOnFailure);
    }

    private static IConfiguration BuildR2Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudflareR2:AccountId"] = "acc123",
                ["CloudflareR2:BucketName"] = "jingle-products",
                ["CloudflareR2:AccessKeyId"] = "ak_test",
                ["CloudflareR2:SecretAccessKey"] = "sk_test_secret",
                ["CloudflareR2:PublicBaseUrl"] = "https://pub-579e9008393e47219443a996a9a7c1d9.r2.dev/"
            })
            .Build();

    private sealed class SingleClientHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
