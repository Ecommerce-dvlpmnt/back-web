using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
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
    private const int MaxActiveWithdrawalStates = 5000;
    private static readonly TimeSpan WithdrawalStateTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<long, WithdrawalState> WithdrawalStates = new();

    public static bool HasActiveWithdrawalState(long telegramUserId) =>
        WithdrawalStates.TryGetValue(telegramUserId, out var state) && !state.IsExpired(DateTime.UtcNow);

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

        CleanupExpiredStates();

        var command = ParseCommand(text) ?? TelegramBotMenu.ResolveCommand(text);
        if (command is not null)
        {
            await HandleCommandAsync(chat.Id, from, command, cancellationToken);
            return;
        }

        if (WithdrawalStates.TryGetValue(from.Id, out var state))
        {
            if (state.IsExpired(DateTime.UtcNow))
            {
                WithdrawalStates.TryRemove(from.Id, out _);
                await SendAsync(
                    chat.Id,
                    "⏳ Час очікування введення вичерпано. Скористайтеся меню внизу.",
                    cancellationToken);
                return;
            }

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
        // Завжди скидаємо будь-який незавершений стан виведення при переході в інший розділ меню або команду
        WithdrawalStates.TryRemove(user.Id, out _);

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

        var safeName = Html(profile.Name);
        var safeCode = Html(profile.ReferralCode);
        var safeLink = Html(BuildReferralLink(profile.ReferralCode));

        var greeting = created
            ? $"Вітаю, <b>{safeName}</b>! Ви зареєстровані як партнер Jingle Decor."
            : $"Вітаю, <b>{safeName}</b>! Ви вже зареєстровані як партнер Jingle Decor.";

        var text = $"""
            {greeting}

            Ваш реферальний код: <code>{safeCode}</code> (натисніть, щоб скопіювати)
            Ваше посилання:
            <code>{safeLink}</code>

            Ви отримуєте <b>{Percent(profile.CommissionPercent)}%</b> від суми кожного замовлення за вашим посиланням.

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

        var safeCode = Html(profile.ReferralCode);
        var safeLink = Html(BuildReferralLink(profile.ReferralCode));

        await SendAsync(
            chatId,
            $"Ваш промокод: <code>{safeCode}</code> (натисніть, щоб скопіювати)\nВаше реферальне посилання:\n<code>{safeLink}</code>",
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

        var savedCard = NormalizeCardNumber(stats.CardNumber);
        var cardLine = savedCard is not null
            ? $"\nКартка: <code>{Html(MaskCardNumber(savedCard))}</code>"
            : string.Empty;

        var text = $"""
            <b>Ваша статистика</b>

            Ім'я: <b>{Html(stats.PartnerName)}</b>
            Код: <code>{Html(stats.ReferralCode)}</code>
            Відсоток: <b>{Percent(stats.CommissionPercent)}%</b>{cardLine}

            <b>Доступно до виведення: {Money(stats.AvailableBalance)} грн</b>
            В холді: {Money(stats.PendingBalance)} грн
            <b>Загальний оборот: {Money(stats.TotalTurnover)} грн</b>

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

        var builder = new StringBuilder("<b>Останні замовлення:</b>\n");
        foreach (var order in orders.Items)
        {
            builder.AppendLine();
            builder.AppendLine($"<b>#{order.OrderNumber} • {Money(order.TotalPrice)} грн</b>");
            builder.AppendLine($"Винагорода: <b>{Money(order.RewardAmount)} грн</b> ({FormatRewardStatus(order.RewardStatus)})");
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
                $"Мінімальна сума для виведення — {Money(MinimumPayoutAmount)} грн.\nДоступно зараз: <b>{Money(profile.AvailableBalance)} грн</b>.",
                cancellationToken);
            return;
        }

        CleanupExpiredStates();
        WithdrawalStates[telegramUserId] = new WithdrawalState
        {
            SavedCardNumber = NormalizeCardNumber(profile.CardNumber)
        };

        await SendAsync(
            chatId,
            $"<b>Доступно до виведення: {Money(profile.AvailableBalance)} грн.</b>\n\nВведіть суму для виведення (мінімум {Money(MinimumPayoutAmount)} грн) або /cancel.",
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
                state.Touch();
                await SendAsync(chatId, "Не вдалося розпізнати суму. Введіть число, наприклад: <code>500</code>", cancellationToken);
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
                state.Touch();
                await SendAsync(chatId, $"Мінімальна сума для виведення — {Money(MinimumPayoutAmount)} грн.", cancellationToken);
                return;
            }

            if (amount > profile.AvailableBalance)
            {
                state.Touch();
                await SendAsync(
                    chatId,
                    $"Недостатньо коштів. Доступно: {Money(profile.AvailableBalance)} грн.",
                    cancellationToken);
                return;
            }

            state.Amount = amount;
            state.SavedCardNumber = NormalizeCardNumber(profile.CardNumber) ?? state.SavedCardNumber;
            state.Touch();

            if (!string.IsNullOrEmpty(state.SavedCardNumber))
            {
                state.AwaitingSavedCardConfirmation = true;
                var masked = Html(MaskCardNumber(state.SavedCardNumber));
                await telegramBotClient.SendMessageAsync(
                    chatId,
                    $"Доступно: <b>{Money(profile.AvailableBalance)} грн</b>.\nСума до виведення: <b>{Money(amount)} грн</b>.\n\nВивести на збережену картку <code>{masked}</code>?",
                    TelegramBotMenu.SavedCardConfirmationReplyMarkup,
                    cancellationToken);
                return;
            }

            state.AwaitingSavedCardConfirmation = false;
            await SendAsync(chatId, "Введіть номер банківської картки (16 цифр) або /cancel.", cancellationToken);
            return;
        }

        if (state.AwaitingSavedCardConfirmation && !string.IsNullOrEmpty(state.SavedCardNumber))
        {
            if (IsConfirmSavedCardInput(text))
            {
                await ExecutePayoutAsync(chatId, user.Id, state.Amount.Value, state.SavedCardNumber, cancellationToken);
                return;
            }

            if (IsEnterNewCardInput(text))
            {
                state.AwaitingSavedCardConfirmation = false;
                state.Touch();
                await SendAsync(chatId, "Введіть номер банківської картки (16 цифр) або /cancel.", cancellationToken);
                return;
            }

            var typedCardDigits = new string(text.Where(char.IsDigit).ToArray());
            if (typedCardDigits.Length == 16)
            {
                await ExecutePayoutAsync(chatId, user.Id, state.Amount.Value, typedCardDigits, cancellationToken);
                return;
            }

            state.Touch();
            var masked = Html(MaskCardNumber(state.SavedCardNumber));
            await telegramBotClient.SendMessageAsync(
                chatId,
                $"Вивести <b>{Money(state.Amount.Value)} грн</b> на збережену картку <code>{masked}</code>?\n\nНатисніть <b>«{TelegramBotMenu.ConfirmSavedCardButton}»</b>, <b>«{TelegramBotMenu.EnterNewCardButton}»</b> або введіть 16 цифр нової картки (чи /cancel).",
                TelegramBotMenu.SavedCardConfirmationReplyMarkup,
                cancellationToken);
            return;
        }

        var cardNumber = new string(text.Where(char.IsDigit).ToArray());
        if (cardNumber.Length != 16)
        {
            state.Touch();
            await SendAsync(chatId, "Номер картки повинен містити рівно 16 цифр. Спробуйте ще раз або /cancel.", cancellationToken);
            return;
        }

        await ExecutePayoutAsync(chatId, user.Id, state.Amount.Value, cardNumber, cancellationToken);
    }

    private async Task ExecutePayoutAsync(
        long chatId,
        long telegramUserId,
        decimal amount,
        string cardNumber,
        CancellationToken cancellationToken)
    {
        // Атомарно видаляємо стан ПЕРЕД викликом БД, щоб унеможливити повторну відправку (double-submit)
        if (!WithdrawalStates.TryRemove(telegramUserId, out _))
        {
            return;
        }

        try
        {
            var payout = await referralService.RequestPayoutAsync(
                telegramUserId,
                new BotPayoutRequestDto
                {
                    Amount = amount,
                    CardNumber = cardNumber
                },
                cancellationToken);

            await SendAsync(
                chatId,
                $"<b>Заявку на виведення {Money(payout.Amount)} грн створено.</b>\nКартка: <code>{Html(payout.CardNumberMasked)}</code>\nСтатус: {FormatPayoutStatus(payout.Status)}.\nЗалишок доступного балансу: <b>{Money(payout.RemainingAvailableBalance)} грн</b>.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не вдалося створити заявку на виплату для TelegramUserId {TelegramUserId}.", telegramUserId);
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

        var builder = new StringBuilder("<b>Останні виплати:</b>\n");
        foreach (var payout in payouts.Take(10))
        {
            builder.AppendLine();
            builder.AppendLine($"<b>{Money(payout.Amount)} грн</b> • {FormatPayoutStatus(payout.Status)}");
            builder.AppendLine($"Картка: <code>{Html(payout.CardNumberMasked)}</code>");
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

    private static void CleanupExpiredStates()
    {
        if (WithdrawalStates.IsEmpty)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var pair in WithdrawalStates)
        {
            if (pair.Value.IsExpired(now))
            {
                WithdrawalStates.TryRemove(pair.Key, out _);
            }
        }

        if (WithdrawalStates.Count > MaxActiveWithdrawalStates)
        {
            var excessCount = WithdrawalStates.Count - MaxActiveWithdrawalStates;
            var oldestKeys = WithdrawalStates
                .OrderBy(pair => pair.Value.UpdatedAtUtc)
                .Take(excessCount)
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var key in oldestKeys)
            {
                WithdrawalStates.TryRemove(key, out _);
            }
        }
    }

    private static string? NormalizeCardNumber(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return null;
        }

        var digits = new string(cardNumber.Where(char.IsDigit).ToArray());
        return digits.Length == 16 ? digits : null;
    }

    private static string MaskCardNumber(string cardNumber)
    {
        var digits = NormalizeCardNumber(cardNumber);
        if (digits is null)
        {
            return cardNumber;
        }

        return $"{digits[..4]} •••• •••• {digits[^4..]}";
    }

    private static bool IsConfirmSavedCardInput(string text)
    {
        var normalized = text.Trim();
        return string.Equals(normalized, TelegramBotMenu.ConfirmSavedCardButton, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "✅ Так, на збережену картку", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "так", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "да", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "+", StringComparison.Ordinal);
    }

    private static bool IsEnterNewCardInput(string text)
    {
        var normalized = text.Trim();
        return string.Equals(normalized, TelegramBotMenu.EnterNewCardButton, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "💳 Вказати іншу картку", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "інша", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "іншу", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "нова", StringComparison.OrdinalIgnoreCase);
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

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
        public string? SavedCardNumber { get; set; }
        public bool AwaitingSavedCardConfirmation { get; set; }
        public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

        public void Touch() => UpdatedAtUtc = DateTime.UtcNow;

        public bool IsExpired(DateTime nowUtc) => nowUtc - UpdatedAtUtc > WithdrawalStateTtl;
    }
}
