using EcommerceBackend.Application.DTOs.Referrals;

namespace EcommerceBackend.Application.Interfaces;

public interface ITelegramBotClient
{
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
}
