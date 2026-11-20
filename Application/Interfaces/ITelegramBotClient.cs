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
}
