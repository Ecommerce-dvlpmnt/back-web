using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Telegram;

namespace EcommerceBackend.Application.Services;

public sealed class TelegramBotClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<TelegramBotClient> logger) : ITelegramBotClient
{
    public const string HttpClientName = "TelegramBot";

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long offset,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        var botToken = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning("Telegram:BotToken не заданий. Отримання оновлень неможливе.");
            return Array.Empty<TelegramUpdate>();
        }

        var payload = new TelegramGetUpdatesRequest(
            offset,
            Math.Clamp(timeoutSeconds, 1, 50),
            ["message"]);

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(
                BuildBotApiUri(botToken, "getUpdates"),
                payload,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Telegram Bot API повернув статус {StatusCode} при отриманні оновлень.",
                    (int)response.StatusCode);
                response.EnsureSuccessStatusCode();
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<TelegramApiResponse<TelegramUpdate[]>>(
                cancellationToken);

            if (apiResponse is null || !apiResponse.Ok)
            {
                logger.LogWarning(
                    "Telegram Bot API повернув неуспішну відповідь: {Description}.",
                    apiResponse?.Description ?? "невідома помилка");
                return Array.Empty<TelegramUpdate>();
            }

            return apiResponse.Result ?? Array.Empty<TelegramUpdate>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Помилка при отриманні оновлень Telegram.");
            throw;
        }
    }

    public async Task<bool> SendMessageAsync(
        long telegramUserId,
        string text,
        CancellationToken cancellationToken = default)
    {
        return await SendMessageCoreAsync(telegramUserId, Guid.Empty, text, null, cancellationToken);
    }

    public async Task<bool> SendMessageAsync(
        long telegramUserId,
        string text,
        TelegramReplyMarkup? replyMarkup,
        CancellationToken cancellationToken = default)
    {
        return await SendMessageCoreAsync(telegramUserId, Guid.Empty, text, replyMarkup, cancellationToken);
    }

    public async Task<bool> SetMyCommandsAsync(
        IReadOnlyList<TelegramBotCommand> commands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var botToken = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning("Telegram:BotToken не заданий. Налаштування меню неможливе.");
            return false;
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(
                BuildBotApiUri(botToken, "setMyCommands"),
                new TelegramSetMyCommandsRequest(commands),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Telegram Bot API повернув статус {StatusCode} при налаштуванні меню команд.",
                    (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Помилка при налаштуванні меню команд Telegram.");
            return false;
        }
    }

    public async Task<bool> SendReferralRewardNotificationAsync(
        ReferralRewardResult reward,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reward);

        var text = string.Format(
            CultureInfo.InvariantCulture,
            "Нове оплачене замовлення #{0} на суму {1:0.00} грн.\nНараховано {2:0.##}% реферальної винагороди: {3:0.00} грн.\nЗагальний баланс: {4:0.00} грн.",
            reward.OrderNumber,
            reward.OrderTotal,
            reward.CommissionPercent,
            reward.RewardAmount,
            reward.PartnerTotalEarned);

        return await SendMessageCoreAsync(reward.PartnerTelegramUserId, reward.PartnerId, text, null, cancellationToken);
    }

    public async Task<bool> SendReferralRewardPendingNotificationAsync(
        ReferralRewardResult reward,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reward);

        var text = string.Format(
            CultureInfo.InvariantCulture,
            "⏳ Нове замовлення #{0} на суму {1:0.00} грн!\nОчікувана реферальна винагорода ({2:0.##}%): +{3:0.00} грн (в холді до отримання клієнтом).",
            reward.OrderNumber,
            reward.OrderTotal,
            reward.CommissionPercent,
            reward.RewardAmount);

        return await SendMessageCoreAsync(reward.PartnerTelegramUserId, reward.PartnerId, text, null, cancellationToken);
    }

    public async Task<bool> SendReferralRewardDeliveredNotificationAsync(
        ReferralRewardResult reward,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reward);

        var text = string.Format(
            CultureInfo.InvariantCulture,
            "💰 Замовлення #{0} успішно вручено клієнту!\nВинагорода +{1:0.00} грн зарахована на ваш баланс до виплати.\nЗагальний доступний баланс: {2:0.00} грн.",
            reward.OrderNumber,
            reward.RewardAmount,
            reward.PartnerTotalEarned);

        return await SendMessageCoreAsync(reward.PartnerTelegramUserId, reward.PartnerId, text, null, cancellationToken);
    }

    public async Task<bool> SendReferralRewardCancelledNotificationAsync(
        ReferralRewardResult reward,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reward);

        var text = string.Format(
            CultureInfo.InvariantCulture,
            "❌ Замовлення #{0} скасовано або повернено ({1}).\nОчікувану винагороду {2:0.00} грн анульовано.\nПоточний баланс: {3:0.00} грн.",
            reward.OrderNumber,
            reason,
            reward.RewardAmount,
            reward.PartnerTotalEarned);

        return await SendMessageCoreAsync(reward.PartnerTelegramUserId, reward.PartnerId, text, null, cancellationToken);
    }

    public async Task<bool> SendPayoutCompletedNotificationAsync(
        long telegramUserId,
        decimal amount,
        string cardNumberMasked,
        CancellationToken cancellationToken = default)
    {
        var text = string.Format(
            CultureInfo.InvariantCulture,
            "💸 Виплату на суму {0:0.00} грн успішно надіслано на вашу картку {1}!\nДякуємо за співпрацю!",
            amount,
            cardNumberMasked);

        return await SendMessageCoreAsync(telegramUserId, Guid.Empty, text, null, cancellationToken);
    }

    public async Task<bool> SendPayoutRejectedNotificationAsync(
        long telegramUserId,
        decimal amount,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var reasonText = string.IsNullOrWhiteSpace(reason) ? "не вказано" : reason.Trim();
        var text = string.Format(
            CultureInfo.InvariantCulture,
            "⚠️ Заявку на виведення {0:0.00} грн відхилено (причина: {1}).\nКошти повернуто на ваш доступний баланс.",
            amount,
            reasonText);

        return await SendMessageCoreAsync(telegramUserId, Guid.Empty, text, null, cancellationToken);
    }

    private async Task<bool> SendMessageCoreAsync(
        long telegramUserId,
        Guid partnerId,
        string text,
        TelegramReplyMarkup? replyMarkup,
        CancellationToken cancellationToken)
    {
        if (telegramUserId <= 0)
        {
            logger.LogWarning(
                "Неможливо надіслати сповіщення в Telegram для партнера {PartnerId}: некоректний TelegramUserId.",
                partnerId);
            return false;
        }

        var botToken = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning(
                "Налаштування Telegram:BotToken не задане. Сповіщення партнеру {PartnerId} пропущено.",
                partnerId);
            return false;
        }

        var requestUri = BuildBotApiUri(botToken, "sendMessage");
        var payload = new TelegramSendMessageRequest(telegramUserId, text, replyMarkup);

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(requestUri, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Telegram Bot API повернув статус {StatusCode} при відправці сповіщення партнеру {PartnerId}.",
                    (int)response.StatusCode,
                    partnerId);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            logger.LogWarning(
                "Помилка при відправці сповіщення в Telegram для партнера {PartnerId}.",
                partnerId);
            return false;
        }
    }

    private string BuildBotApiUri(string botToken, string method)
    {
        var configuredBaseUrl = configuration["Telegram:BaseUrl"];
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? "https://api.telegram.org"
            : configuredBaseUrl.Trim().TrimEnd('/');

        return baseUrl.EndsWith("/bot", StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl}{botToken}/{method}"
            : $"{baseUrl}/bot{botToken}/{method}";
    }

    private sealed record TelegramSendMessageRequest(
        [property: JsonPropertyName("chat_id")] long ChatId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("reply_markup")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        TelegramReplyMarkup? ReplyMarkup = null);

    private sealed record TelegramSetMyCommandsRequest(
        [property: JsonPropertyName("commands")] IReadOnlyList<TelegramBotCommand> Commands);

    private sealed record TelegramGetUpdatesRequest(
        [property: JsonPropertyName("offset")] long Offset,
        [property: JsonPropertyName("timeout")] int Timeout,
        [property: JsonPropertyName("allowed_updates")] string[] AllowedUpdates);

    private sealed record TelegramApiResponse<T>(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] T? Result,
        [property: JsonPropertyName("description")] string? Description);
}
