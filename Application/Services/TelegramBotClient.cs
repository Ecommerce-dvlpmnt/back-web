using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;

namespace EcommerceBackend.Application.Services;

public sealed class TelegramBotClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<TelegramBotClient> logger) : ITelegramBotClient
{
    public const string HttpClientName = "TelegramBot";

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

        return await SendMessageAsync(reward.PartnerTelegramUserId, reward.PartnerId, text, cancellationToken);
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

        return await SendMessageAsync(reward.PartnerTelegramUserId, reward.PartnerId, text, cancellationToken);
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

        return await SendMessageAsync(reward.PartnerTelegramUserId, reward.PartnerId, text, cancellationToken);
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

        return await SendMessageAsync(reward.PartnerTelegramUserId, reward.PartnerId, text, cancellationToken);
    }

    private async Task<bool> SendMessageAsync(
        long telegramUserId,
        Guid partnerId,
        string text,
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

        var configuredBaseUrl = configuration["Telegram:BaseUrl"];
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? "https://api.telegram.org"
            : configuredBaseUrl.Trim().TrimEnd('/');

        var requestUri = baseUrl.EndsWith("/bot", StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl}{botToken}/sendMessage"
            : $"{baseUrl}/bot{botToken}/sendMessage";

        var payload = new TelegramSendMessageRequest(telegramUserId, text);

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

    private sealed record TelegramSendMessageRequest(
        [property: JsonPropertyName("chat_id")] long ChatId,
        [property: JsonPropertyName("text")] string Text);
}
