using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Telegram;
using EcommerceBackend.Domain.Enums;

namespace EcommerceBackend.Application.Services;

public sealed class TelegramBotCommandHandler(
    IReferralService referralService,
    ITelegramBotClient telegramBotClient,
    IConfiguration configuration,
    ILogger<TelegramBotCommandHandler> logger)
{
    private const decimal MinimumPayoutAmount = 300m;
    private static readonly ConcurrentDictionary<long, WithdrawalState> WithdrawalStates = new();

    public async Task HandleUpdateAsync(TelegramUpdate update, CancellationToken cancellationToken = default)
    {
        if (update.Message is not
            {
                From: { IsBot: false } from,
                Chat: { } chat,
                Text: { } rawText
            })
        {
            return;
        }

        if (!string.Equals(chat.Type, "private", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var text = rawText.Trim();
        if (text.Length == 0)
        {
            return;
        }

        var command = ParseCommand(text) ?? TelegramBotMenu.ResolveCommand(text);
        if (command is not null)
        {
            await HandleCommandAsync(chat.Id, from, command, cancellationToken);
            return;
        }

        if (WithdrawalStates.TryGetValue(from.Id, out var state))
        {
            await HandleWithdrawalInputAsync(chat.Id, from, state, text, cancellationToken);
            return;
        }

        await SendMenuHintAsync(chat.Id, cancellationToken);
    }

    private async Task HandleCommandAsync(
        long chatId,
        TelegramUser user,
        string command,
        CancellationToken cancellationToken)
    {
        switch (command)
        {
            case "/start":
                await StartAsync(chatId, user, cancellationToken);
                break;
            case "/link":
                await SendLinkAsync(chatId, user.Id, cancellationToken);
                break;
            case "/stats":
                await SendStatsAsync(chatId, user.Id, cancellationToken);
                break;
            case "/orders":
                await SendOrdersAsync(chatId, user.Id, cancellationToken);
                break;
            case "/withdraw":
                await StartWithdrawalAsync(chatId, user.Id, cancellationToken);
                break;
            case "/payouts":
                await SendPayoutsAsync(chatId, user.Id, cancellationToken);
                break;
            case "/cancel":
                WithdrawalStates.TryRemove(user.Id, out _);
                await SendAsync(chatId, "Дію скасовано.", cancellationToken);
                break;
            default:
                await SendMenuHintAsync(chatId, cancellationToken);
                break;
        }
    }

    private async Task StartAsync(long chatId, TelegramUser user, CancellationToken cancellationToken)
    {
        var (profile, created) = await EnsurePartnerAsync(user, cancellationToken);

        var greeting = created
            ? $"Вітаю, {profile.Name}! Ви зареєстровані як партнер Jingle Decor."
            : $"Вітаю, {profile.Name}! Ви вже зареєстровані як партнер Jingle Decor.";

        var text = $"""
            {greeting}

            Ваш реферальний код: {profile.ReferralCode}
            Ваше посилання:
            {BuildReferralLink(profile.ReferralCode)}

            Ви отримуєте {Percent(profile.CommissionPercent)}% від суми кожного замовлення за вашим посиланням.

            Скористайтеся меню внизу, щоб керувати ботом.
            """;

        await SendAsync(chatId, text, cancellationToken);
    }

    private async Task SendLinkAsync(long chatId, long telegramUserId, CancellationToken cancellationToken)
    {
        var profile = await referralService.GetBotPartnerProfileAsync(telegramUserId, cancellationToken);
        if (profile is null)
        {
            await SendNotRegisteredAsync(chatId, cancellationToken);
            return;
        }

        await SendAsync(
            chatId,
            $"Ваше реферальне посилання:\n{BuildReferralLink(profile.ReferralCode)}",
            cancellationToken);
    }

    private async Task SendStatsAsync(long chatId, long telegramUserId, CancellationToken cancellationToken)
    {
        var stats = await referralService.GetBotPartnerStatsAsync(telegramUserId, cancellationToken);
        if (stats is null)
        {
            await SendNotRegisteredAsync(chatId, cancellationToken);
            return;
        }

        var text = $"""
            Ваша статистика

            Ім'я: {stats.PartnerName}
            Код: {stats.ReferralCode}
            Відсоток: {Percent(stats.CommissionPercent)}%

            Доступно до виведення: {Money(stats.AvailableBalance)} грн
            В холді: {Money(stats.PendingBalance)} грн
            Загальний оборот: {Money(stats.TotalTurnover)} грн

            Замовлень: {stats.TotalOrders}
            Доставлено: {stats.DeliveredOrders}
            В обробці: {stats.PendingOrders}
            Скасовано: {stats.CancelledOrders}
            """;

        await SendAsync(chatId, text, cancellationToken);
    }

    private async Task SendOrdersAsync(long chatId, long telegramUserId, CancellationToken cancellationToken)
    {
        var orders = await referralService.GetBotPartnerOrdersAsync(telegramUserId, 10, 0, cancellationToken);
        if (orders is null)
        {
            await SendNotRegisteredAsync(chatId, cancellationToken);
            return;
        }

        if (orders.Items.Count == 0)
        {
            await SendAsync(chatId, "У вас ще немає замовлень.", cancellationToken);
            return;
        }

        var builder = new StringBuilder("Останні замовлення:\n");
        foreach (var order in orders.Items)
        {
            builder.AppendLine();
            builder.AppendLine($"#{order.OrderNumber} • {Money(order.TotalPrice)} грн");
            builder.AppendLine($"Винагорода: {Money(order.RewardAmount)} грн ({FormatRewardStatus(order.RewardStatus)})");
            builder.AppendLine($"Статус: {FormatOrderStatus(order.OrderStatus)}");
            builder.Append($"Дата: {order.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}");
        }

        if (orders.TotalCount > orders.Items.Count)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append($"Показано {orders.Items.Count} з {orders.TotalCount}.");
        }

        await SendAsync(chatId, builder.ToString(), cancellationToken);
    }

    private async Task StartWithdrawalAsync(long chatId, long telegramUserId, CancellationToken cancellationToken)
    {
        var profile = await referralService.GetBotPartnerProfileAsync(telegramUserId, cancellationToken);
        if (profile is null)
        {
            await SendNotRegisteredAsync(chatId, cancellationToken);
            return;
        }

        if (profile.AvailableBalance < MinimumPayoutAmount)
        {
            await SendAsync(
                chatId,
                $"Мінімальна сума для виведення — {Money(MinimumPayoutAmount)} грн.\nДоступно зараз: {Money(profile.AvailableBalance)} грн.",
                cancellationToken);
            return;
        }

        WithdrawalStates[telegramUserId] = new WithdrawalState();
        await SendAsync(
            chatId,
            $"Доступно до виведення: {Money(profile.AvailableBalance)} грн.\n\nВведіть суму для виведення (мінімум {Money(MinimumPayoutAmount)} грн) або /cancel.",
            cancellationToken);
    }

    private async Task HandleWithdrawalInputAsync(
        long chatId,
        TelegramUser user,
        WithdrawalState state,
        string text,
        CancellationToken cancellationToken)
    {
        if (state.Amount is null)
        {
            if (!TryParseAmount(text, out var amount))
            {
                await SendAsync(chatId, "Не вдалося розпізнати суму. Введіть число, наприклад: 500", cancellationToken);
                return;
            }

            var profile = await referralService.GetBotPartnerProfileAsync(user.Id, cancellationToken);
            if (profile is null)
            {
                WithdrawalStates.TryRemove(user.Id, out _);
                await SendNotRegisteredAsync(chatId, cancellationToken);
                return;
            }

            if (amount < MinimumPayoutAmount)
            {
                await SendAsync(chatId, $"Мінімальна сума для виведення — {Money(MinimumPayoutAmount)} грн.", cancellationToken);
                return;
            }

            if (amount > profile.AvailableBalance)
            {
                await SendAsync(
                    chatId,
                    $"Недостатньо коштів. Доступно: {Money(profile.AvailableBalance)} грн.",
                    cancellationToken);
                return;
            }

            state.Amount = amount;
            await SendAsync(chatId, "Введіть номер банківської картки (16 цифр) або /cancel.", cancellationToken);
            return;
        }

        var cardNumber = new string(text.Where(char.IsDigit).ToArray());
        if (cardNumber.Length != 16)
        {
            await SendAsync(chatId, "Номер картки повинен містити рівно 16 цифр. Спробуйте ще раз або /cancel.", cancellationToken);
            return;
        }

        try
        {
            var payout = await referralService.RequestPayoutAsync(
                user.Id,
                new BotPayoutRequestDto
                {
                    Amount = state.Amount.Value,
                    CardNumber = cardNumber
                },
                cancellationToken);

            WithdrawalStates.TryRemove(user.Id, out _);
            await SendAsync(
                chatId,
                $"Заявку на виведення {Money(payout.Amount)} грн створено.\nКартка: {payout.CardNumberMasked}\nСтатус: {FormatPayoutStatus(payout.Status)}.\nЗалишок доступного балансу: {Money(payout.RemainingAvailableBalance)} грн.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не вдалося створити заявку на виплату для TelegramUserId {TelegramUserId}.", user.Id);
            WithdrawalStates.TryRemove(user.Id, out _);
            await SendAsync(
                chatId,
                "Не вдалося створити заявку на виведення. Перевірте суму та номер картки і спробуйте ще раз.",
                cancellationToken);
        }
    }

    private async Task SendPayoutsAsync(long chatId, long telegramUserId, CancellationToken cancellationToken)
    {
        var payouts = await referralService.GetBotPartnerPayoutsAsync(telegramUserId, cancellationToken);
        if (payouts.Count == 0)
        {
            await SendAsync(chatId, "Історія виплат порожня.", cancellationToken);
            return;
        }

        var builder = new StringBuilder("Останні виплати:\n");
        foreach (var payout in payouts.Take(10))
        {
            builder.AppendLine();
            builder.AppendLine($"{Money(payout.Amount)} грн • {FormatPayoutStatus(payout.Status)}");
            builder.AppendLine($"Картка: {payout.CardNumberMasked}");
            builder.Append($"Дата: {payout.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}");
        }

        await SendAsync(chatId, builder.ToString(), cancellationToken);
    }

    private async Task SendMenuHintAsync(long chatId, CancellationToken cancellationToken)
    {
        await SendAsync(chatId, "Скористайтеся меню внизу.", cancellationToken);
    }

    private async Task SendNotRegisteredAsync(long chatId, CancellationToken cancellationToken)
    {
        await SendAsync(chatId, "Спочатку зареєструйтесь командою /start.", cancellationToken);
    }

    private async Task<(BotPartnerProfileResponse Profile, bool Created)> EnsurePartnerAsync(
        TelegramUser user,
        CancellationToken cancellationToken)
    {
        var existing = await referralService.GetBotPartnerProfileAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        try
        {
            var created = await referralService.RegisterBotPartnerAsync(
                new BotRegisterPartnerRequest
                {
                    TelegramUserId = user.Id,
                    Name = BuildPartnerName(user)
                },
                cancellationToken);

            return (created, true);
        }
        catch (InvalidOperationException)
        {
            var raced = await referralService.GetBotPartnerProfileAsync(user.Id, cancellationToken);
            if (raced is not null)
            {
                return (raced, false);
            }

            throw;
        }
    }

    private string BuildReferralLink(string referralCode)
    {
        var configuredSiteUrl = configuration["Referral:PublicSiteUrl"];
        var siteUrl = string.IsNullOrWhiteSpace(configuredSiteUrl)
            ? "https://front-web-pi.vercel.app"
            : configuredSiteUrl.Trim();

        return $"{siteUrl.TrimEnd('/')}/?ref={Uri.EscapeDataString(referralCode)}";
    }

    private Task<bool> SendAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        return telegramBotClient.SendMessageAsync(chatId, text, TelegramBotMenu.ReplyMarkup, cancellationToken);
    }

    private static string? ParseCommand(string text)
    {
        if (!text.StartsWith('/'))
        {
            return null;
        }

        var command = text.Split(' ', 2)[0];
        var atIndex = command.IndexOf('@');
        if (atIndex >= 0)
        {
            command = command[..atIndex];
        }

        return command.ToLowerInvariant();
    }

    private static bool TryParseAmount(string text, out decimal amount)
    {
        var normalized = text
            .Replace(" ", string.Empty)
            .Replace(',', '.');

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out amount);
    }

    private static string BuildPartnerName(TelegramUser user)
    {
        var name = string.Join(
            ' ',
            new[] { user.FirstName, user.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim()));

        if (string.IsNullOrWhiteSpace(name))
        {
            name = user.Username?.Trim() ?? string.Empty;
        }

        if (name.Length < 2)
        {
            name = "Партнер";
        }

        return name.Length > 100 ? name[..100] : name;
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Percent(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatOrderStatus(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.New => "Нове",
            OrderStatus.Confirmed => "Підтверджено",
            OrderStatus.Packed => "Упаковано",
            OrderStatus.Shipped => "Відправлено",
            OrderStatus.Delivered => "Доставлено",
            OrderStatus.Cancelled => "Скасовано",
            OrderStatus.Refused => "Відмова",
            OrderStatus.Returned => "Повернено",
            _ => status.ToString()
        };
    }

    private static string FormatRewardStatus(ReferralRewardStatus status)
    {
        return status switch
        {
            ReferralRewardStatus.None => "—",
            ReferralRewardStatus.Pending => "в холді",
            ReferralRewardStatus.Paid => "зараховано",
            ReferralRewardStatus.Cancelled => "анульовано",
            _ => status.ToString()
        };
    }

    private static string FormatPayoutStatus(PayoutRequestStatus status)
    {
        return status switch
        {
            PayoutRequestStatus.Pending => "в обробці",
            PayoutRequestStatus.Completed => "виплачено",
            PayoutRequestStatus.Rejected => "відхилено",
            _ => status.ToString()
        };
    }

    private sealed class WithdrawalState
    {
        public decimal? Amount { get; set; }
    }
}
