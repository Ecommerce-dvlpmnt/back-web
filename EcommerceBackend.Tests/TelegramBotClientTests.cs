using System.Net;
using System.Text;
using System.Text.Json;
using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class TelegramBotClientTests
{
    [Fact]
    public async Task GetUpdatesAsync_ParsesTelegramUpdatesAndSendsLongPollingRequest()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "ok": true,
                      "result": [
                        {
                          "update_id": 100,
                          "message": {
                            "message_id": 5,
                            "from": {
                              "id": 777,
                              "is_bot": false,
                              "first_name": "Sasha",
                              "username": "sasha"
                            },
                            "chat": {
                              "id": 777,
                              "type": "private"
                            },
                            "date": 1700000000,
                            "text": "/start"
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            });
        var client = CreateClient(
            handler,
            new Dictionary<string, string?>
            {
                ["Telegram:BaseUrl"] = "https://api.telegram.org/",
                ["Telegram:BotToken"] = "123456:ABC-TestToken"
            });

        var updates = await client.GetUpdatesAsync(99, 10);

        Assert.Single(updates);
        Assert.Equal(100, updates[0].UpdateId);
        Assert.Equal("/start", updates[0].Message?.Text);
        Assert.Equal(777, updates[0].Message?.From?.Id);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            "https://api.telegram.org/bot123456:ABC-TestToken/getUpdates",
            handler.LastRequestUri?.ToString());

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(99, doc.RootElement.GetProperty("offset").GetInt64());
        Assert.Equal(10, doc.RootElement.GetProperty("timeout").GetInt32());
        Assert.Equal("message", doc.RootElement.GetProperty("allowed_updates")[0].GetString());
    }

    [Fact]
    public async Task SendMessageAsync_SendsMessageToChat()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            });
        var client = CreateClient(
            handler,
            new Dictionary<string, string?>
            {
                ["Telegram:BaseUrl"] = "https://api.telegram.org",
                ["Telegram:BotToken"] = "123456:ABC-TestToken"
            });

        var sent = await client.SendMessageAsync(777, "Hello");

        Assert.True(sent);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(777, doc.RootElement.GetProperty("chat_id").GetInt64());
        Assert.Equal("Hello", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal("HTML", doc.RootElement.GetProperty("parse_mode").GetString());
    }

    [Fact]
    public async Task SendReferralRewardNotificationAsync_SendsPostRequestWithExpectedPayload()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            });
        var client = CreateClient(
            handler,
            new Dictionary<string, string?>
            {
                ["Telegram:BaseUrl"] = "https://api.telegram.org/",
                ["Telegram:BotToken"] = "123456:ABC-TestToken"
            });

        var reward = CreateSampleReward();

        var sent = await client.SendReferralRewardNotificationAsync(reward);

        Assert.True(sent);
        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal(
            "https://api.telegram.org/bot123456:ABC-TestToken/sendMessage",
            handler.LastRequestUri?.ToString());

        Assert.NotNull(handler.LastRequestBody);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal(987654321L, doc.RootElement.GetProperty("chat_id").GetInt64());
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.NotNull(text);
        Assert.Contains("#42", text);
        Assert.Contains("5%", text);
        Assert.Contains("150.00", text);
        Assert.Contains("450.00", text);
    }

    [Fact]
    public async Task SendReferralRewardPendingNotificationAsync_SendsPendingMessage()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            });
        var client = CreateClient(
            handler,
            new Dictionary<string, string?>
            {
                ["Telegram:BaseUrl"] = "https://api.telegram.org/",
                ["Telegram:BotToken"] = "123456:ABC-TestToken"
            });

        var reward = CreateSampleReward();

        var sent = await client.SendReferralRewardPendingNotificationAsync(reward);

        Assert.True(sent);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.NotNull(text);
        Assert.Contains("#42", text);
        Assert.Contains("холді", text);
        Assert.Contains("150.00", text);
    }

    [Fact]
    public async Task SendReferralRewardDeliveredNotificationAsync_SendsDeliveredMessage()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            });
        var client = CreateClient(
            handler,
            new Dictionary<string, string?>
            {
                ["Telegram:BaseUrl"] = "https://api.telegram.org/",
                ["Telegram:BotToken"] = "123456:ABC-TestToken"
            });

        var reward = CreateSampleReward();

        var sent = await client.SendReferralRewardDeliveredNotificationAsync(reward);

        Assert.True(sent);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.NotNull(text);
        Assert.Contains("#42", text);
        Assert.Contains("вручено", text);
        Assert.Contains("450.00", text);
    }

    [Fact]
    public async Task SendReferralRewardCancelledNotificationAsync_SendsCancelledMessage()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"ok\":true}", Encoding.UTF8, "application/json")
            });
        var client = CreateClient(
            handler,
            new Dictionary<string, string?>
            {
                ["Telegram:BaseUrl"] = "https://api.telegram.org/",
                ["Telegram:BotToken"] = "123456:ABC-TestToken"
            });

        var reward = CreateSampleReward();

        var sent = await client.SendReferralRewardCancelledNotificationAsync(reward, "відмова на пошті");

        Assert.True(sent);
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var text = doc.RootElement.GetProperty("text").GetString();
        Assert.NotNull(text);
        Assert.Contains("#42", text);
        Assert.Contains("скасовано або повернено", text);
        Assert.Contains("відмова на пошті", text);
    }

    [Fact]
    public async Task SendReferralRewardNotificationAsync_ReturnsFalse_WhenBotTokenIsMissing()
    {
        var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(
            handler,
            new Dictionary<string, string?>
            {
                ["Telegram:BaseUrl"] = "https://api.telegram.org",
                ["Telegram:BotToken"] = ""
            });

        var sent = await client.SendReferralRewardNotificationAsync(CreateSampleReward());

        Assert.False(sent);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task SendReferralRewardNotificationAsync_ReturnsFalse_WhenTelegramReturnsErrorStatus()
    {
        var handler = new RecordingHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = CreateClient(
            handler,
            new Dictionary<string, string?>
            {
                ["Telegram:BaseUrl"] = "https://api.telegram.org",
                ["Telegram:BotToken"] = "123456:ABC-TestToken"
            });

        var sent = await client.SendReferralRewardNotificationAsync(CreateSampleReward());

        Assert.False(sent);
        Assert.NotNull(handler.LastRequestUri);
    }

    [Fact]
    public async Task SendReferralRewardNotificationAsync_ReturnsFalse_WhenHttpClientThrows()
    {
        var handler = new RecordingHttpMessageHandler(
            () => throw new HttpRequestException("Network failure"));
        var client = CreateClient(
            handler,
            new Dictionary<string, string?>
            {
                ["Telegram:BaseUrl"] = "https://api.telegram.org",
                ["Telegram:BotToken"] = "123456:ABC-TestToken"
            });

        var sent = await client.SendReferralRewardNotificationAsync(CreateSampleReward());

        Assert.False(sent);
    }

    private static TelegramBotClient CreateClient(
        RecordingHttpMessageHandler handler,
        Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new TelegramBotClient(
            new StubHttpClientFactory(handler),
            configuration,
            NullLogger<TelegramBotClient>.Instance);
    }

    private static ReferralRewardResult CreateSampleReward()
    {
        return new ReferralRewardResult(
            OrderId: Guid.NewGuid(),
            OrderNumber: 42,
            PartnerId: Guid.NewGuid(),
            PartnerTelegramUserId: 987654321L,
            PartnerName: "Max",
            OrderTotal: 3000.00m,
            CommissionPercent: 5.0m,
            RewardAmount: 150.00m,
            PartnerTotalEarned: 450.00m);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;

        public RecordingHttpMessageHandler(HttpResponseMessage response)
        {
            _responseFactory = () => response;
        }

        public RecordingHttpMessageHandler(Func<HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return _responseFactory();
        }
    }
}
