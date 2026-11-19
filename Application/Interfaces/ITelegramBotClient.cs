using EcommerceBackend.Application.DTOs.Referrals;

namespace EcommerceBackend.Application.Interfaces;

public interface ITelegramBotClient
{
    Task<bool> SendReferralRewardNotificationAsync(
        ReferralRewardResult reward,
        CancellationToken cancellationToken = default);
}
