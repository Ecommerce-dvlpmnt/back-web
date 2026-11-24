using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Telegram;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.Services;

public sealed class TelegramBotClient(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<TelegramBotClient> logger) : ITelegramBotClient
{
    public const string HttpClientName = "TelegramBot";
    private static readonly TimeSpan FastApiRequestTimeout = TimeSpan.FromSeconds(10);

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
            ["message", "callback_query"]);

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
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FastApiRequestTimeout);

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(
                BuildBotApiUri(botToken, "setMyCommands"),
                new TelegramSetMyCommandsRequest(commands),
                timeoutCts.Token);

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
            "<b>Нове оплачене замовлення #{0}</b> на суму <b>{1:0.00} грн</b>.\nНараховано {2:0.##}% реферальної винагороди: <b>{3:0.00} грн</b>.\nЗагальний баланс: <b>{4:0.00} грн</b>.",
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
            "⏳ <b>Нове замовлення #{0}</b> на суму <b>{1:0.00} грн</b>!\nОчікувана реферальна винагорода ({2:0.##}%): <b>+{3:0.00} грн</b> (в холді до отримання клієнтом).",
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
            "💰 <b>Замовлення #{0} успішно вручено клієнту!</b>\nВинагорода <b>+{1:0.00} грн</b> зарахована на ваш баланс до виплати.\nЗагальний доступний баланс: <b>{2:0.00} грн</b>.",
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

        var safeReason = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(reason) ? "скасовано" : reason.Trim());
        var text = string.Format(
            CultureInfo.InvariantCulture,
            "❌ <b>Замовлення #{0} скасовано або повернено</b> ({1}).\nОчікувану винагороду <b>{2:0.00} грн</b> анульовано.\nПоточний баланс: <b>{3:0.00} грн</b>.",
            reward.OrderNumber,
            safeReason,
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
        var safeCard = WebUtility.HtmlEncode(cardNumberMasked ?? string.Empty);
        var text = string.Format(
            CultureInfo.InvariantCulture,
            "💸 Виплату на суму <b>{0:0.00} грн</b> успішно надіслано на вашу картку <code>{1}</code>!\nДякуємо за співпрацю!",
            amount,
            safeCard);

        return await SendMessageCoreAsync(telegramUserId, Guid.Empty, text, null, cancellationToken);
    }

    public async Task<bool> SendPayoutRejectedNotificationAsync(
        long telegramUserId,
        decimal amount,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var reasonText = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(reason) ? "не вказано" : reason.Trim());
        var text = string.Format(
            CultureInfo.InvariantCulture,
            "⚠️ Заявку на виведення <b>{0:0.00} грн</b> відхилено (причина: {1}).\nКошти повернуто на ваш доступний баланс.",
            amount,
            reasonText);

        return await SendMessageCoreAsync(telegramUserId, Guid.Empty, text, null, cancellationToken);
    }

    public async Task<bool> SendMessageWithInlineKeyboardAsync(
        long telegramUserId,
        string text,
        TelegramInlineKeyboardMarkup inlineMarkup,
        CancellationToken cancellationToken = default)
    {
        return await SendMessageCoreAsync(telegramUserId, Guid.Empty, text, inlineMarkup, cancellationToken);
    }

    public async Task<bool> EditMessageTextAsync(
        long chatId,
        long messageId,
        string text,
        TelegramInlineKeyboardMarkup? inlineMarkup = null,
        CancellationToken cancellationToken = default)
    {
        if (chatId <= 0)
        {
            return false;
        }

        if (messageId <= 0)
        {
            return await SendMessageCoreAsync(chatId, Guid.Empty, text, inlineMarkup, cancellationToken);
        }

        var botToken = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return false;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FastApiRequestTimeout);

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(
                BuildBotApiUri(botToken, "editMessageText"),
                new TelegramEditMessageTextRequest(chatId, messageId, text, "HTML", inlineMarkup),
                timeoutCts.Token);

            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Помилка при редагуванні повідомлення {MessageId} у чаті {ChatId}.", messageId, chatId);
            return false;
        }
    }

    public async Task<bool> AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(callbackQueryId))
        {
            return false;
        }

        var botToken = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return false;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FastApiRequestTimeout);

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(
                BuildBotApiUri(botToken, "answerCallbackQuery"),
                new TelegramAnswerCallbackQueryRequest(callbackQueryId, text),
                timeoutCts.Token);

            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Помилка при відповіді на callback_query {CallbackQueryId}.", callbackQueryId);
            return false;
        }
    }

    public async Task<bool> DeleteMessageAsync(
        long chatId,
        long messageId,
        CancellationToken cancellationToken = default)
    {
        if (chatId <= 0 || messageId <= 0)
        {
            return false;
        }

        var botToken = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return false;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FastApiRequestTimeout);

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(
                BuildBotApiUri(botToken, "deleteMessage"),
                new TelegramDeleteMessageRequest(chatId, messageId),
                timeoutCts.Token);

            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Помилка при видаленні повідомлення {MessageId} у чаті {ChatId}.", messageId, chatId);
            return false;
        }
    }

    public async Task<bool> SendAdminNewOrderNotificationAsync(
        AdminBotOrderNotificationDto order,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        var adminIds = TelegramBotMenu.GetAdminUserIds(configuration);
        if (adminIds.Count == 0)
        {
            return false;
        }

        var text = FormatAdminOrderMessage(order, isNewOrderNotification: true);
        var anySent = false;

        foreach (var adminId in adminIds)
        {
            if (await SendMessageCoreAsync(adminId, Guid.Empty, text, null, cancellationToken))
            {
                anySent = true;
            }
        }

        return anySent;
    }

    public async Task<bool> SendAdminPayoutRequestNotificationAsync(
        AdminPayoutResponse payout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payout);

        var adminIds = TelegramBotMenu.GetAdminUserIds(configuration);
        if (adminIds.Count == 0)
        {
            return false;
        }

        var text = FormatAdminPayoutRequestMessage(payout);
        var inlineMarkup = CreatePayoutApprovalInlineMarkup(payout.Id);
        var anySent = false;

        foreach (var adminId in adminIds)
        {
            if (await SendMessageCoreAsync(adminId, payout.PartnerId, text, inlineMarkup, cancellationToken))
            {
                anySent = true;
            }
        }

        return anySent;
    }

    public static string FormatAdminOrderMessage(AdminBotOrderNotificationDto order, bool isNewOrderNotification = false)
    {
        var builder = new StringBuilder();
        var headerPrefix = isNewOrderNotification ? "🛍 <b>Нове замовлення" : "📦 <b>Замовлення";
        builder.AppendLine($"{headerPrefix} #{order.OrderNumber}</b> (<code>{WebUtility.HtmlEncode(order.PublicId)}</code>)");
        builder.AppendLine($"👤 Клієнт: <b>{WebUtility.HtmlEncode(order.CustomerFullName)}</b>");
        builder.AppendLine($"📞 Телефон: <code>{WebUtility.HtmlEncode(order.CustomerPhone)}</code>");

        if (!string.IsNullOrWhiteSpace(order.DeliveryCityName) || !string.IsNullOrWhiteSpace(order.DeliveryWarehouseName))
        {
            var delivery = string.Join(
                ", ",
                new[] { order.DeliveryCityName, order.DeliveryWarehouseName }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => WebUtility.HtmlEncode(s!.Trim())));
            builder.AppendLine($"📍 Доставка: {delivery}");
        }

        builder.AppendLine();
        builder.AppendLine("📋 <b>Позиції:</b>");
        foreach (var item in order.Items)
        {
            builder.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "• {0} × {1} шт. — <b>{2:0.00} грн</b> ({3:0.00} грн/шт.)",
                    WebUtility.HtmlEncode(item.ProductName),
                    item.Quantity,
                    item.LineTotal,
                    item.UnitPrice));
        }

        builder.AppendLine();
        if (!string.IsNullOrWhiteSpace(order.PromoCode))
        {
            var percentText = order.PromoDiscountPercent.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "-{0:0.##}%", order.PromoDiscountPercent.Value)
                : "знижка";
            builder.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "🎟 Промокод: <code>{0}</code> ({1}, -{2:0.00} грн)",
                    WebUtility.HtmlEncode(order.PromoCode),
                    percentText,
                    order.DiscountAmount));
        }

        if (!string.IsNullOrWhiteSpace(order.ReferrerCode))
        {
            var refName = string.IsNullOrWhiteSpace(order.ReferrerName)
                ? string.Empty
                : $"{WebUtility.HtmlEncode(order.ReferrerName)} ";
            builder.AppendLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "🤝 Реферал: {0}(<code>{1}</code>) • бонус: <b>{2:0.00} грн</b>",
                    refName,
                    WebUtility.HtmlEncode(order.ReferrerCode),
                    order.ReferralRewardAmount));
        }

        if (string.IsNullOrWhiteSpace(order.PromoCode) && string.IsNullOrWhiteSpace(order.ReferrerCode))
        {
            builder.AppendLine("🏷 Промо/Реферал: <i>пряме замовлення</i>");
        }

        builder.Append(
            string.Format(
                CultureInfo.InvariantCulture,
                "💰 <b>Загальна сума: {0:0.00} грн</b>",
                order.TotalPrice));

        return builder.ToString();
    }

    public static string FormatAdminPayoutRequestMessage(AdminPayoutResponse payout)
    {
        var holderLine = !string.IsNullOrWhiteSpace(payout.CardHolderName)
            ? $"\n🏦 Отримувач: <b>{WebUtility.HtmlEncode(payout.CardHolderName)}</b>"
            : string.Empty;

        return string.Format(
            CultureInfo.InvariantCulture,
            "💸 <b>Заявка на виведення коштів</b>\n\n👤 Реферал: <b>{0}</b> (<code>{1}</code>)\n💰 Сума: <b>{2:0.00} грн</b>\n💳 Картка (натисніть для копіювання):\n<code>{3}</code>{4}\n📅 Дата: {5:dd.MM.yyyy HH:mm}",
            WebUtility.HtmlEncode(payout.PartnerName),
            WebUtility.HtmlEncode(payout.PartnerReferralCode),
            payout.Amount,
            WebUtility.HtmlEncode(payout.CardNumber),
            holderLine,
            payout.CreatedAt.ToLocalTime());
    }

    public static TelegramInlineKeyboardMarkup CreatePayoutApprovalInlineMarkup(Guid payoutId)
    {
        return new TelegramInlineKeyboardMarkup(
        [
            [
                new TelegramInlineKeyboardButton("✅ Виплачено", $"payout_ok:{payoutId:N}"),
                new TelegramInlineKeyboardButton("❌ Відхилити", $"payout_no:{payoutId:N}")
            ]
        ]);
    }

    private async Task<bool> SendMessageCoreAsync(
        long telegramUserId,
        Guid partnerId,
        string text,
        object? replyMarkup,
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
        var payload = new TelegramSendMessageRequest(telegramUserId, text, "HTML", replyMarkup);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FastApiRequestTimeout);

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync(requestUri, payload, timeoutCts.Token);

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

    public async Task<TelegramWebhookInfo?> GetWebhookInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var botToken = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning("Telegram:BotToken не заданий. Отримання інформації про вебхук неможливе.");
            return null;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FastApiRequestTimeout);

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(
                BuildBotApiUri(botToken, "getWebhookInfo"),
                timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                logger.LogWarning("Telegram getWebhookInfo повернув помилку {StatusCode}: {Body}", (int)response.StatusCode, body);
                return null;
            }

            var apiResponse = await response.Content.ReadFromJsonAsync<TelegramApiResponse<TelegramWebhookInfo>>(
                cancellationToken: timeoutCts.Token);

            return apiResponse is { Ok: true } ? apiResponse.Result : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Помилка виклику Telegram getWebhookInfo.");
            return null;
        }
    }

    public Task<bool> SetWebhookAsync(
        string webhookUrl,
        string? secretToken = null,
        CancellationToken cancellationToken = default) =>
        SetWebhookAsync(webhookUrl, secretToken, false, cancellationToken);

    public async Task<bool> SetWebhookAsync(
        string webhookUrl,
        string? secretToken,
        bool dropPendingUpdates,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(webhookUrl);

        var botToken = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning("Telegram:BotToken не заданий. Налаштування вебхука неможливе.");
            return false;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FastApiRequestTimeout);

            var client = httpClientFactory.CreateClient(HttpClientName);
            var payload = new Dictionary<string, object?>
            {
                ["url"] = webhookUrl.Trim(),
                ["allowed_updates"] = new[] { "message", "callback_query" },
                ["drop_pending_updates"] = dropPendingUpdates
            };

            if (!string.IsNullOrWhiteSpace(secretToken))
            {
                payload["secret_token"] = secretToken.Trim();
            }

            using var response = await client.PostAsJsonAsync(
                BuildBotApiUri(botToken, "setWebhook"),
                payload,
                timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                logger.LogWarning("Telegram setWebhook повернув помилку {StatusCode}: {Body}", (int)response.StatusCode, body);
                return false;
            }

            logger.LogInformation("Telegram webhook успішно встановлено на {WebhookUrl}.", webhookUrl);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Помилка виклику Telegram setWebhook.");
            return false;
        }
    }

    public Task<bool> DeleteWebhookAsync(
        CancellationToken cancellationToken = default) =>
        DeleteWebhookAsync(false, cancellationToken);

    public async Task<bool> DeleteWebhookAsync(
        bool dropPendingUpdates,
        CancellationToken cancellationToken = default)
    {
        var botToken = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return false;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(FastApiRequestTimeout);

            var client = httpClientFactory.CreateClient(HttpClientName);
            var payload = new { drop_pending_updates = dropPendingUpdates };

            using var response = await client.PostAsJsonAsync(
                BuildBotApiUri(botToken, "deleteWebhook"),
                payload,
                timeoutCts.Token);

            logger.LogInformation("Telegram webhook видалено (перехід на Long Polling).");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Помилка виклику Telegram deleteWebhook.");
            return false;
        }
    }

    private sealed record TelegramSendMessageRequest(
        [property: JsonPropertyName("chat_id")] long ChatId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("parse_mode")] string ParseMode = "HTML",
        [property: JsonPropertyName("reply_markup")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        object? ReplyMarkup = null);

    private sealed record TelegramEditMessageTextRequest(
        [property: JsonPropertyName("chat_id")] long ChatId,
        [property: JsonPropertyName("message_id")] long MessageId,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("parse_mode")] string ParseMode = "HTML",
        [property: JsonPropertyName("reply_markup")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        object? ReplyMarkup = null);

    private sealed record TelegramAnswerCallbackQueryRequest(
        [property: JsonPropertyName("callback_query_id")] string CallbackQueryId,
        [property: JsonPropertyName("text")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Text = null);

    private sealed record TelegramDeleteMessageRequest(
        [property: JsonPropertyName("chat_id")] long ChatId,
        [property: JsonPropertyName("message_id")] long MessageId);

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
