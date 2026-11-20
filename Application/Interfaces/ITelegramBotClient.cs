using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Telegram;

namespace EcommerceBackend.Application.Interfaces;

public interface ITelegramBotClient
{
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(
        long offset,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default);

    Task<bool> SendMessageAsync(
        long telegramUserId,
        string text,
        CancellationToken cancellationToken = default);

    Task<bool> SendMessageAsync(
        long telegramUserId,
        string text,
        TelegramReplyMarkup? replyMarkup,
        CancellationToken cancellationToken = default);

    Task<bool> SetMyCommandsAsync(
        IReadOnlyList<TelegramBotCommand> commands,
        CancellationToken cancellationToken = default);

    Task<bool> SendReferralRewardNotificationAsync(
        ReferralRewardResult reward,
        CancellationToken cancellationToken = default);

    Task<bool> SendReferralRewardPendingNotificationAsync(
        ReferralRewardResult reward,
        CancellationToken cancellationToken = default);

    Task<bool> SendReferralRewardDeliveredNotificationAsync(
        ReferralRewardResult reward,
        CancellationToken cancellationToken = default);

    Task<bool> SendReferralRewardCancelledNotificationAsync(
        ReferralRewardResult reward,
        string reason,
        CancellationToken cancellationToken = default);

    Task<bool> SendPayoutCompletedNotificationAsync(
        long telegramUserId,
        decimal amount,
        string cardNumberMasked,
        CancellationToken cancellationToken = default);

    Task<bool> SendPayoutRejectedNotificationAsync(
        long telegramUserId,
        decimal amount,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<bool> SendMessageWithInlineKeyboardAsync(
        long telegramUserId,
        string text,
        TelegramInlineKeyboardMarkup inlineMarkup,
        CancellationToken cancellationToken = default) =>
        SendMessageAsync(telegramUserId, text, cancellationToken);

    Task<bool> EditMessageTextAsync(
        long chatId,
        long messageId,
        string text,
        TelegramInlineKeyboardMarkup? inlineMarkup = null,
        CancellationToken cancellationToken = default) =>
        inlineMarkup is not null
            ? SendMessageWithInlineKeyboardAsync(chatId, text, inlineMarkup, cancellationToken)
            : SendMessageAsync(chatId, text, cancellationToken);

    Task<bool> AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    Task<bool> SendAdminNewOrderNotificationAsync(
        AdminBotOrderNotificationDto order,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    Task<bool> SendAdminPayoutRequestNotificationAsync(
        AdminPayoutResponse payout,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
