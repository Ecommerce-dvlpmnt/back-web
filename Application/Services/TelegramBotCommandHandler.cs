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
    private static readonly ConcurrentDictionary<long, AdminPromoCreationState> AdminPromoStates = new();

    public static bool HasActiveWithdrawalState(long telegramUserId) =>
        WithdrawalStates.TryGetValue(telegramUserId, out var state) && !state.IsExpired(DateTime.UtcNow);

    public static bool HasActiveAdminPromoState(long telegramUserId) =>
        AdminPromoStates.TryGetValue(telegramUserId, out var state) && !state.IsExpired(DateTime.UtcNow);

    public async Task HandleUpdateAsync(TelegramUpdate update, CancellationToken cancellationToken = default)
    {
        CleanupExpiredStates();

        if (update.CallbackQuery is { From: { IsBot: false } cbUser, Data: { Length: > 0 } cbData } callbackQuery)
        {
            await HandleCallbackQueryAsync(callbackQuery, cbUser, cbData, cancellationToken);
            return;
        }

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

        var isAdmin = TelegramBotMenu.IsAdmin(configuration, from.Id);
        var command = ParseCommand(text)
            ?? (isAdmin ? TelegramBotMenu.ResolveAdminCommand(text) : null)
            ?? TelegramBotMenu.ResolveCommand(text);

        if (command is not null)
        {
            await HandleCommandAsync(chat.Id, from, command, text, isAdmin, cancellationToken);
            return;
        }

        if (isAdmin && AdminPromoStates.TryGetValue(from.Id, out var promoState))
        {
            if (promoState.IsExpired(DateTime.UtcNow))
            {
                AdminPromoStates.TryRemove(from.Id, out _);
                await SendAdminAsync(
                    chat.Id,
                    "⏳ Час очікування створення промокоду вичерпано. Скористайтеся меню внизу.",
                    cancellationToken);
                return;
            }

            await HandleAdminPromoCreationInputAsync(chat.Id, from, promoState, text, cancellationToken);
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

        if (isAdmin)
        {
            await SendAdminAsync(chat.Id, "Оберіть розділ адмін-панелі в меню внизу.", cancellationToken);
            return;
        }

        await SendMenuHintAsync(chat.Id, cancellationToken);
    }

    private async Task HandleCommandAsync(
        long chatId,
        TelegramUser user,
        string command,
        string rawText,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        // Завжди скидаємо будь-які незавершені стани при переході в інший розділ меню або команду
        WithdrawalStates.TryRemove(user.Id, out _);
        AdminPromoStates.TryRemove(user.Id, out _);

        if (command is "/myid" or "/id")
        {
            var replyMarkup = isAdmin ? TelegramBotMenu.AdminReplyMarkup : TelegramBotMenu.ReplyMarkup;
            await telegramBotClient.SendMessageAsync(
                chatId,
                $"Ваш Telegram ID: <code>{user.Id}</code>",
                replyMarkup,
                cancellationToken);
            return;
        }

        if (isAdmin)
        {
            switch (command)
            {
                case "/start":
                case "/admin":
                    await StartAdminAsync(chatId, user, cancellationToken);
                    return;
                case "/admin_orders":
                case "/orders":
                    await SendAdminOrdersAsync(chatId, cancellationToken);
                    return;
                case "/admin_referrals":
                case "/stats":
                    await SendAdminReferralsAsync(chatId, cancellationToken);
                    return;
                case "/admin_payouts":
                case "/payouts":
                    await SendAdminPayoutsAsync(chatId, cancellationToken);
                    return;
                case "/admin_promos":
                    await SendAdminPromoCodesAsync(chatId, cancellationToken);
                    return;
                case "/admin_create_promo":
                    await StartAdminCreatePromoAsync(chatId, user.Id, cancellationToken);
                    return;
                case "/cancel":
                    await SendAdminAsync(chatId, "Дію скасовано.", cancellationToken);
                    return;
                default:
                    await SendAdminAsync(chatId, "Оберіть розділ адмін-панелі в меню внизу.", cancellationToken);
                    return;
            }
        }

        switch (command)
        {
            case "/start":
                await StartAsync(chatId, user, rawText, cancellationToken);
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

    private async Task StartAsync(long chatId, TelegramUser user, string rawText, CancellationToken cancellationToken)
    {
        var existing = await referralService.GetBotPartnerProfileAsync(user.Id, cancellationToken);
        if (existing is not null)
        {
            await SendPartnerWelcomeAsync(chatId, existing, created: false, cancellationToken);
            return;
        }

        var inviteCode = ExtractStartPayload(rawText);
        var inviter = inviteCode is not null
            ? await referralService.FindPartnerByCodeAsync(inviteCode, cancellationToken)
            : null;

        var safeUserName = Html(BuildPartnerName(user));
        var header = inviter is not null
            ? $"👋 Вітаю, <b>{safeUserName}</b>!\nПартнер <b>{Html(inviter.Name)}</b> запрошує вас до партнерської програми <b>Jingle Decor</b>."
            : $"👋 Вітаю, <b>{safeUserName}</b>, у партнерській програмі <b>Jingle Decor</b>!";

        var promptText = $"""
            {header}

            Бажаєте стати партнером та отримувати <b>3%</b> від суми кожного доставленого замовлення за вашим особистим посиланням?
            """;

        var confirmCallbackData = inviter is not null
            ? $"partner_reg_yes:{inviter.ReferralCode}"
            : "partner_reg_yes";

        var optInMarkup = new TelegramInlineKeyboardMarkup(
        [
            [
                new TelegramInlineKeyboardButton("✅ Так, стати партнером", confirmCallbackData),
                new TelegramInlineKeyboardButton("❌ Ні, дякую", "partner_reg_no")
            ]
        ]);

        await telegramBotClient.SendMessageWithInlineKeyboardAsync(chatId, promptText, optInMarkup, cancellationToken);
    }

    private async Task SendPartnerWelcomeAsync(
        long chatId,
        BotPartnerProfileResponse profile,
        bool created,
        CancellationToken cancellationToken)
    {
        var safeName = Html(profile.Name);
        var safeCode = Html(profile.ReferralCode);
        var safeStoreLink = Html(BuildReferralLink(profile.ReferralCode));
        var safeBotInviteLink = Html(BuildBotInviteLink(profile.ReferralCode));

        var greeting = created
            ? $"Вітаю, <b>{safeName}</b>! Ви зареєстровані як партнер Jingle Decor."
            : $"Вітаю, <b>{safeName}</b>! Ви вже зареєстровані як партнер Jingle Decor.";

        var text = $"""
            {greeting}

            Ваш реферальний код: <code>{safeCode}</code> (натисніть, щоб скопіювати)
            🛍 Посилання на магазин для покупців:
            <code>{safeStoreLink}</code>

            Ви отримуєте <b>{Percent(profile.CommissionPercent)}%</b> від суми кожного доставленого замовлення за вашим посиланням.
            ⏳ Бонус потрапляє в холд і стає доступним одразу після отримання посилки клієнтом на Новій Пошті.
            💳 Мінімальна сума виведення — <b>300 грн</b> на будь-яку українську картку.

            🤝 Посилання на бота для друзів (вони отримуватимуть 3% зі своїх замовлень):
            <code>{safeBotInviteLink}</code>

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
        var safeBotInviteLink = Html(BuildBotInviteLink(profile.ReferralCode));

        await SendAsync(
            chatId,
            $"Ваш реферальний код: <code>{safeCode}</code> (ваш бонус: <b>{Percent(profile.CommissionPercent)}%</b>)\n\n🛍 Посилання на магазин (для покупців):\n<code>{safeLink}</code>\n\n🤝 Запросити друга стати партнером у боті (на 3%):\n<code>{safeBotInviteLink}</code>",
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
                if (!ReferralService.IsValidCardLuhn(typedCardDigits))
                {
                    state.Touch();
                    await SendAsync(chatId, "Некоректний номер картки (перевірте правильність уведених 16 цифр). Спробуйте ще раз або /cancel.", cancellationToken);
                    return;
                }

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
        if (cardNumber.Length != 16 || !ReferralService.IsValidCardLuhn(cardNumber))
        {
            state.Touch();
            var errorText = cardNumber.Length != 16
                ? "Номер картки повинен містити рівно 16 цифр. Спробуйте ще раз або /cancel."
                : "Некоректний номер картки (перевірте правильність уведених 16 цифр). Спробуйте ще раз або /cancel.";
            await SendAsync(chatId, errorText, cancellationToken);
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

    private async Task StartAdminAsync(long chatId, TelegramUser user, CancellationToken cancellationToken)
    {
        var safeName = Html(BuildPartnerName(user));
        var text = $"""
            🛡 <b>Адмін-панель Jingle Decor</b>
            Вітаю, <b>{safeName}</b>! Ви увійшли як адміністратор.

            • <b>{TelegramBotMenu.AdminOrdersButton}</b> — всі замовлення по позиціях, суми, промокоди та реферали
            • <b>{TelegramBotMenu.AdminReferralsButton}</b> — хто скільки людей привів та баланс рефералів
            • <b>{TelegramBotMenu.AdminPayoutsButton}</b> — заявки на виведення коштів (підтвердження в 1 клік)
            • <b>{TelegramBotMenu.AdminPromosButton}</b> — активні промокоди та ліміти
            • <b>{TelegramBotMenu.AdminCreatePromoButton}</b> — створити промокод на знижку для рідних/знайомих

            Усі нові замовлення та заявки на виплату також приходять сюди автоматично.
            """;

        await SendAdminAsync(chatId, text, cancellationToken);
    }

    private async Task SendAdminOrdersAsync(long chatId, CancellationToken cancellationToken)
    {
        await SendOrEditAdminOrdersListAsync(chatId, messageId: null, filter: "all", page: 0, cancellationToken);
    }

    private async Task SendOrEditAdminOrdersListAsync(
        long chatId,
        long? messageId,
        string filter,
        int page,
        CancellationToken cancellationToken)
    {
        var normalizedFilter = (filter ?? "all").Trim().ToLowerInvariant();
        if (normalizedFilter is not ("today" or "yesterday" or "7d" or "all"))
        {
            normalizedFilter = "all";
        }

        var pageData = await referralService.GetAdminBotOrdersPageAsync(
            normalizedFilter,
            Math.Max(0, page),
            5,
            cancellationToken);

        var filterTitle = normalizedFilter switch
        {
            "today" => "Сьогодні",
            "yesterday" => "Вчора",
            "7d" => "За 7 днів",
            _ => "Усі замовлення"
        };

        var totalPages = Math.Max(1, (int)Math.Ceiling(pageData.TotalOrdersCount / (double)Math.Max(1, pageData.PageSize)));

        var builder = new StringBuilder();
        builder.AppendLine($"📦 <b>Замовлення ({filterTitle})</b>");
        builder.AppendLine($"📊 Замовлень: <b>{pageData.TotalOrdersCount} шт.</b> • Сума: <b>{Money(pageData.TotalRevenue)} грн</b>");

        if (pageData.Items.Count == 0)
        {
            builder.AppendLine();
            builder.Append("<i>За обраний період замовлень немає.</i>");
        }
        else
        {
            builder.AppendLine($"📄 Сторінка: <b>{pageData.Page + 1} з {totalPages}</b>");
            builder.AppendLine();
            builder.Append("<i>Натисніть на замовлення нижче, щоб відкрити позиції, клієнта та промо/реферала:</i>");
        }

        var rows = new List<IReadOnlyList<TelegramInlineKeyboardButton>>
        {
            new[]
            {
                new TelegramInlineKeyboardButton(
                    normalizedFilter == "today" ? "✅ Сьогодні" : "Сьогодні",
                    "ord_list:today:0"),
                new TelegramInlineKeyboardButton(
                    normalizedFilter == "yesterday" ? "✅ Вчора" : "Вчора",
                    "ord_list:yesterday:0"),
                new TelegramInlineKeyboardButton(
                    normalizedFilter == "7d" ? "✅ 7 днів" : "7 днів",
                    "ord_list:7d:0"),
                new TelegramInlineKeyboardButton(
                    normalizedFilter == "all" ? "✅ Усі" : "Усі",
                    "ord_list:all:0")
            }
        };

        foreach (var order in pageData.Items)
        {
            var badge = !string.IsNullOrWhiteSpace(order.PromoCode)
                ? " 🎟"
                : !string.IsNullOrWhiteSpace(order.ReferrerCode)
                    ? " 🤝"
                    : string.Empty;

            var customerShort = string.IsNullOrWhiteSpace(order.CustomerFullName)
                ? "Клієнт"
                : order.CustomerFullName.Length > 16
                    ? order.CustomerFullName[..16] + "…"
                    : order.CustomerFullName;

            var payBadge = FormatPaymentStatusBadge(order.PaymentMethod, order.PaymentStatus);
            var buttonLabel = $"{FormatOrderStatusEmoji(order.OrderStatus)} {payBadge} #{order.OrderNumber} • {customerShort} • {Money(order.TotalPrice)} грн{badge}";
            rows.Add(
            [
                new TelegramInlineKeyboardButton(
                    buttonLabel,
                    $"ord_view:{order.OrderNumber}:{normalizedFilter}:{pageData.Page}")
            ]);
        }

        if (totalPages > 1)
        {
            var navButtons = new List<TelegramInlineKeyboardButton>();
            if (pageData.Page > 0)
            {
                navButtons.Add(new TelegramInlineKeyboardButton(
                    "⬅️ Попередні 5",
                    $"ord_list:{normalizedFilter}:{pageData.Page - 1}"));
            }

            if (pageData.Page + 1 < totalPages)
            {
                navButtons.Add(new TelegramInlineKeyboardButton(
                    "Наступні 5 ➡️",
                    $"ord_list:{normalizedFilter}:{pageData.Page + 1}"));
            }

            if (navButtons.Count > 0)
            {
                rows.Add(navButtons);
            }
        }

        rows.Add(
        [
            new TelegramInlineKeyboardButton("❌ Закрити", "ord_close")
        ]);

        var markup = new TelegramInlineKeyboardMarkup(rows);
        if (messageId is > 0)
        {
            await telegramBotClient.EditMessageTextAsync(chatId, messageId.Value, builder.ToString(), markup, cancellationToken);
        }
        else
        {
            await telegramBotClient.SendMessageWithInlineKeyboardAsync(chatId, builder.ToString(), markup, cancellationToken);
        }
    }

    private async Task SendOrEditAdminOrderDetailAsync(
        long chatId,
        long? messageId,
        int orderNumber,
        string filter,
        int page,
        CancellationToken cancellationToken)
    {
        var order = await referralService.GetAdminBotOrderByNumberAsync(orderNumber, cancellationToken);
        var backMarkup = new TelegramInlineKeyboardMarkup(
        [
            [
                new TelegramInlineKeyboardButton("🔙 До списку", $"ord_list:{filter}:{page}"),
                new TelegramInlineKeyboardButton("❌ Закрити", "ord_close")
            ]
        ]);

        if (order is null)
        {
            const string notFoundText = "⚠️ Замовлення не знайдено.";
            if (messageId is > 0)
            {
                await telegramBotClient.EditMessageTextAsync(chatId, messageId.Value, notFoundText, backMarkup, cancellationToken);
            }
            else
            {
                await telegramBotClient.SendMessageWithInlineKeyboardAsync(chatId, notFoundText, backMarkup, cancellationToken);
            }
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine(TelegramBotClient.FormatAdminOrderMessage(order, isNewOrderNotification: false));
        builder.AppendLine();
        builder.Append($"📌 Статус: <b>{FormatOrderStatus(order.OrderStatus)}</b> • {order.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}");

        if (messageId is > 0)
        {
            await telegramBotClient.EditMessageTextAsync(chatId, messageId.Value, builder.ToString(), backMarkup, cancellationToken);
        }
        else
        {
            await telegramBotClient.SendMessageWithInlineKeyboardAsync(chatId, builder.ToString(), backMarkup, cancellationToken);
        }
    }

    private async Task SendAdminReferralsAsync(long chatId, CancellationToken cancellationToken, long? editMessageId = null)
    {
        var partners = await referralService.GetAllPartnersAsync(cancellationToken);
        if (partners.Count == 0)
        {
            await SendAdminAsync(chatId, "Зареєстрованих рефералів поки немає.", cancellationToken);
            return;
        }

        var builder = new StringBuilder($"🤝 <b>Реферали ({partners.Count}):</b>\n");
        var rows = new List<IReadOnlyList<TelegramInlineKeyboardButton>>();

        foreach (var partner in partners)
        {
            var cardInfo = !string.IsNullOrWhiteSpace(partner.CardNumber)
                ? $"<code>{Html(MaskCardNumber(partner.CardNumber))}</code>"
                : "<i>не вказана</i>";

            builder.AppendLine();
            builder.AppendLine($"👤 <b>{Html(partner.Name)}</b> • код: <code>{Html(partner.ReferralCode)}</code> ({Percent(partner.CommissionPercent)}%)");
            if (!string.IsNullOrWhiteSpace(partner.InvitedByPartnerName))
            {
                var inviterCode = !string.IsNullOrWhiteSpace(partner.InvitedByReferralCode)
                    ? $" (<code>{Html(partner.InvitedByReferralCode)}</code>)"
                    : string.Empty;
                builder.AppendLine($"🔗 Привів: <b>{Html(partner.InvitedByPartnerName)}</b>{inviterCode}");
            }
            builder.AppendLine($"👥 Запрошено замовлень: <b>{partner.TotalOrders}</b> (доставлено: <b>{partner.DeliveredOrders}</b>)");
            builder.AppendLine($"📈 Оборот: <b>{Money(partner.TotalTurnover)} грн</b>");
            builder.AppendLine($"💰 Доступно: <b>{Money(partner.AvailableBalance)} грн</b> • В холді: {Money(partner.PendingBalance)} грн");
            if (partner.InvitedPartnersCount > 0)
            {
                var namesList = partner.InvitedPartnerNames is { Count: > 0 }
                    ? $" ({string.Join(", ", partner.InvitedPartnerNames.Take(5).Select(Html))}{(partner.InvitedPartnerNames.Count > 5 ? "…" : string.Empty)})"
                    : string.Empty;
                builder.AppendLine($"🤝 Запросив у бот: <b>{partner.InvitedPartnersCount}</b>{namesList}");
                builder.AppendLine($"📊 Оборот його запрошених: <b>{Money(partner.InvitedPartnersTurnover)} грн</b> • їхній бонус: <b>{Money(partner.InvitedPartnersEarned)} грн</b>");
            }
            builder.Append($"💳 Картка: {cardInfo}");
            builder.AppendLine();

            if (rows.Count < 10)
            {
                var targetPct = partner.CommissionPercent >= 5m ? 3 : 5;
                var icon = targetPct == 5 ? "⬆️" : "⬇️";
                var shortName = partner.Name.Length > 14 ? partner.Name[..14] + "…" : partner.Name;
                rows.Add(
                [
                    new TelegramInlineKeyboardButton(
                        $"{icon} {shortName}: поставити {targetPct}%",
                        $"partner_pct:{partner.Id:N}:{targetPct}")
                ]);
            }
        }

        rows.Add([new TelegramInlineKeyboardButton("❌ Закрити", "msg_close")]);
        var markup = new TelegramInlineKeyboardMarkup(rows);

        if (editMessageId is > 0)
        {
            await telegramBotClient.EditMessageTextAsync(chatId, editMessageId.Value, builder.ToString(), markup, cancellationToken);
        }
        else
        {
            await telegramBotClient.SendMessageWithInlineKeyboardAsync(chatId, builder.ToString(), markup, cancellationToken);
        }
    }

    private async Task SendAdminPayoutsAsync(long chatId, CancellationToken cancellationToken)
    {
        var pendingPayouts = await referralService.GetAllPayoutRequestsAsync(PayoutRequestStatus.Pending, cancellationToken);
        if (pendingPayouts.Count > 0)
        {
            await SendAdminAsync(chatId, $"💸 <b>Активні заявки на виплату ({pendingPayouts.Count}):</b>", cancellationToken);
            foreach (var payout in pendingPayouts.Take(10))
            {
                var text = TelegramBotClient.FormatAdminPayoutRequestMessage(payout);
                var markup = TelegramBotClient.CreatePayoutApprovalInlineMarkup(payout.Id);
                await telegramBotClient.SendMessageWithInlineKeyboardAsync(chatId, text, markup, cancellationToken);
            }
            return;
        }

        var allPayouts = await referralService.GetAllPayoutRequestsAsync(null, cancellationToken);
        if (allPayouts.Count == 0)
        {
            await SendAdminAsync(chatId, "Заявок на виплату поки немає.", cancellationToken);
            return;
        }

        var builder = new StringBuilder("💸 <b>Активних заявок немає. Останні оброблені виплати:</b>\n");
        foreach (var payout in allPayouts.Take(10))
        {
            builder.AppendLine();
            builder.AppendLine($"• <b>{Html(payout.PartnerName)}</b> (<code>{Html(payout.PartnerReferralCode)}</code>) — <b>{Money(payout.Amount)} грн</b>");
            builder.AppendLine($"  Картка: <code>{Html(MaskCardNumber(payout.CardNumber))}</code> • Статус: <b>{FormatPayoutStatus(payout.Status)}</b>");
            builder.Append($"  Дата: {payout.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}");
        }

        await SendAdminAsync(chatId, builder.ToString(), cancellationToken);
    }

    private async Task SendAdminPromoCodesAsync(long chatId, CancellationToken cancellationToken)
    {
        var promos = await referralService.GetAllPromoCodesAsync(cancellationToken);
        if (promos.Count == 0)
        {
            await SendAdminAsync(
                chatId,
                $"Активних промокодів поки немає.\nНатисніть <b>«{TelegramBotMenu.AdminCreatePromoButton}»</b>, щоб створити промокод на знижку.",
                cancellationToken);
            return;
        }

        var builder = new StringBuilder($"🎟 <b>Промокоди ({promos.Count}):</b>\n");
        var activeButtons = new List<IReadOnlyList<TelegramInlineKeyboardButton>>();

        foreach (var promo in promos.Take(15))
        {
            var limitText = promo.MaxUses.HasValue && promo.MaxUses.Value > 0
                ? $"{promo.UsedCount} / {promo.MaxUses.Value}"
                : $"{promo.UsedCount} / ∞ (безліміт)";
            var statusIcon = promo.IsActive ? "🟢 Активний" : "🔴 Вимкнений";

            builder.AppendLine();
            builder.AppendLine($"• <code>{Html(promo.Code)}</code> — знижка <b>-{Percent(promo.DiscountPercent)}%</b> ({statusIcon})");
            builder.AppendLine($"  Використано: <b>{limitText}</b>");
            builder.Append($"  Посилання: <code>{Html(BuildReferralLink(promo.Code))}</code>");
            builder.AppendLine();

            if (promo.IsActive && activeButtons.Count < 8)
            {
                activeButtons.Add(
                [
                    new TelegramInlineKeyboardButton($"🛑 Вимкнути {promo.Code}", $"promo_off:{promo.Id:N}")
                ]);
            }
        }

        activeButtons.Add(
        [
            new TelegramInlineKeyboardButton("❌ Закрити", "promo_close")
        ]);

        await telegramBotClient.SendMessageWithInlineKeyboardAsync(
            chatId,
            builder.ToString(),
            new TelegramInlineKeyboardMarkup(activeButtons),
            cancellationToken);
    }

    private async Task StartAdminCreatePromoAsync(long chatId, long adminTelegramUserId, CancellationToken cancellationToken)
    {
        CleanupExpiredStates();
        AdminPromoStates[adminTelegramUserId] = new AdminPromoCreationState();

        var cancelMarkup = new TelegramInlineKeyboardMarkup(
        [
            [
                new TelegramInlineKeyboardButton("❌ Скасувати створення", "promo_create_cancel")
            ]
        ]);

        await telegramBotClient.SendMessageWithInlineKeyboardAsync(
            chatId,
            "🎟 <b>Створення промокоду для рідних/знайомих (Крок 1 з 3)</b>\n\nВведіть назву промокоду (наприклад: <code>FAMILY15</code>, <code>BRO20</code>) або напишіть <code>авто</code> для автоматичної генерації (чи /cancel):",
            cancelMarkup,
            cancellationToken);
    }

    private async Task HandleAdminPromoCreationInputAsync(
        long chatId,
        TelegramUser user,
        AdminPromoCreationState state,
        string text,
        CancellationToken cancellationToken)
    {
        if (!state.CodeStepCompleted)
        {
            var rawCode = text.Trim();
            if (string.Equals(rawCode, "авто", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rawCode, "auto", StringComparison.OrdinalIgnoreCase))
            {
                state.Code = null;
                state.CodeStepCompleted = true;
                state.Touch();
            }
            else
            {
                var normalized = rawCode.ToUpperInvariant();
                if (normalized.Length is < 2 or > 32 || !normalized.All(c => char.IsLetterOrDigit(c) || c is '_' or '-'))
                {
                    state.Touch();
                    await SendAdminAsync(
                        chatId,
                        "Код має містити від 2 до 32 символів (латинські літери, цифри, <code>_</code> або <code>-</code>), або напишіть <code>авто</code> (чи /cancel).",
                        cancellationToken);
                    return;
                }

                state.Code = normalized;
                state.CodeStepCompleted = true;
                state.Touch();
            }

            await SendAdminAsync(
                chatId,
                "🎟 <b>Крок 2 з 3: Відсоток знижки</b>\n\nВведіть відсоток знижки для покупця (від <code>1</code> до <code>99</code>, наприклад: <code>15</code>) або /cancel:",
                cancellationToken);
            return;
        }

        if (state.DiscountPercent is null)
        {
            var cleanedPercent = text.Replace("%", string.Empty).Trim();
            if (!TryParseAmount(cleanedPercent, out var percent) || percent <= 0m || percent >= 100m)
            {
                state.Touch();
                await SendAdminAsync(
                    chatId,
                    "Некоректний відсоток. Введіть число від <code>1</code> до <code>99</code> (наприклад: <code>15</code>) або /cancel:",
                    cancellationToken);
                return;
            }

            state.DiscountPercent = Math.Round(percent, 2, MidpointRounding.AwayFromZero);
            state.Touch();

            await SendAdminAsync(
                chatId,
                "🎟 <b>Крок 3 з 3: Ліміт використань</b>\n\nВведіть максимальну кількість замовлень за цим промокодом (наприклад: <code>1</code> — одноразовий, <code>5</code> — на 5 замовлень, або <code>0</code> — безлімітний):",
                cancellationToken);
            return;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxUses) || maxUses < 0 || maxUses > 1_000_000)
        {
            state.Touch();
            await SendAdminAsync(
                chatId,
                "Введіть ціле число від <code>0</code> (безліміт) до <code>1000000</code> (наприклад: <code>1</code> або <code>5</code>) чи /cancel:",
                cancellationToken);
            return;
        }

        // Атомарно видаляємо стан ПЕРЕД збереженням у БД (захист від double-submit)
        if (!AdminPromoStates.TryRemove(user.Id, out _))
        {
            return;
        }

        try
        {
            var created = await referralService.CreatePromoCodeAsync(
                new CreatePromoCodeRequest
                {
                    Code = state.Code,
                    DiscountPercent = state.DiscountPercent.Value,
                    MaxUses = maxUses == 0 ? null : maxUses,
                    CreatedByTelegramUserId = user.Id
                },
                cancellationToken);

            var limitLabel = created.MaxUses.HasValue
                ? $"{created.MaxUses.Value} замовлень"
                : "Безліміт";
            var link = Html(BuildReferralLink(created.Code));

            await SendAdminAsync(
                chatId,
                $"""
                ✅ <b>Промокод успішно створено!</b>

                🎟 Код: <code>{Html(created.Code)}</code> (натисніть для копіювання)
                📉 Знижка покупцю: <b>-{Percent(created.DiscountPercent)}%</b>
                🔢 Ліміт використань: <b>{limitLabel}</b>

                🔗 Або пряме посилання зі знижкою:
                <code>{link}</code>
                """,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не вдалося створити промокод для адміна {TelegramUserId}.", user.Id);
            await SendAdminAsync(
                chatId,
                $"❌ Не вдалося створити промокод: {Html(ex.Message)}",
                cancellationToken);
        }
    }

    private async Task HandleCallbackQueryAsync(
        TelegramCallbackQuery callbackQuery,
        TelegramUser user,
        string data,
        CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message?.Chat?.Id ?? user.Id;

        if (data is "partner_reg_yes" || data.StartsWith("partner_reg_yes:", StringComparison.OrdinalIgnoreCase))
        {
            if (TelegramBotMenu.IsAdmin(configuration, user.Id))
            {
                await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Ви є адміністратором.", cancellationToken);
                return;
            }

            var inviterCode = data.Contains(':')
                ? data[(data.IndexOf(':') + 1)..].Trim()
                : null;

            var (profile, created) = await EnsurePartnerAsync(user, inviterCode, cancellationToken);
            await telegramBotClient.AnswerCallbackQueryAsync(
                callbackQuery.Id,
                created ? "🎉 Вітаємо в команді Jingle Decor!" : "Ви вже зареєстровані!",
                cancellationToken);

            if (created && !string.IsNullOrWhiteSpace(inviterCode))
            {
                var inviter = await referralService.FindPartnerByCodeAsync(inviterCode, cancellationToken);
                if (inviter is not null && inviter.TelegramUserId != user.Id)
                {
                    _ = telegramBotClient.SendMessageAsync(
                        inviter.TelegramUserId,
                        $"🤝 За вашим посиланням до партнерської програми приєднався новий партнер: <b>{Html(profile.Name)}</b>!",
                        CancellationToken.None);
                }
            }

            if (callbackQuery.Message?.MessageId is { } msgId)
            {
                var deleted = await telegramBotClient.DeleteMessageAsync(chatId, msgId, cancellationToken);
                if (!deleted)
                {
                    await telegramBotClient.EditMessageTextAsync(
                        chatId,
                        msgId,
                        "✅ <b>Участь у партнерській програмі підтверджено!</b>",
                        inlineMarkup: null,
                        cancellationToken);
                }
            }

            await SendPartnerWelcomeAsync(chatId, profile, created, cancellationToken);
            return;
        }

        if (data is "partner_reg_no")
        {
            await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Скасовано", cancellationToken);
            if (callbackQuery.Message?.MessageId is { } msgId)
            {
                await telegramBotClient.EditMessageTextAsync(
                    chatId,
                    msgId,
                    "Добре! Якщо передумаєте — натисніть /start у будь-який момент.",
                    inlineMarkup: null,
                    cancellationToken);
            }
            return;
        }

        if (!TelegramBotMenu.IsAdmin(configuration, user.Id))
        {
            await telegramBotClient.AnswerCallbackQueryAsync(
                callbackQuery.Id,
                "⛔ Доступ лише для адміністраторів.",
                cancellationToken);
            return;
        }

        if (data is "ord_close" or "promo_close" or "msg_close")
        {
            await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Закрито", cancellationToken);
            if (callbackQuery.Message?.MessageId is { } msgId)
            {
                var deleted = await telegramBotClient.DeleteMessageAsync(chatId, msgId, cancellationToken);
                if (!deleted)
                {
                    await telegramBotClient.EditMessageTextAsync(
                        chatId,
                        msgId,
                        "<i>Вікно закрито.</i>",
                        inlineMarkup: null,
                        cancellationToken);
                }
            }
            return;
        }

        if (data is "promo_create_cancel")
        {
            AdminPromoStates.TryRemove(user.Id, out _);
            await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Створення скасовано.", cancellationToken);
            if (callbackQuery.Message?.MessageId is { } msgId)
            {
                var deleted = await telegramBotClient.DeleteMessageAsync(chatId, msgId, cancellationToken);
                if (!deleted)
                {
                    await telegramBotClient.EditMessageTextAsync(
                        chatId,
                        msgId,
                        "<i>Створення промокоду скасовано.</i>",
                        inlineMarkup: null,
                        cancellationToken);
                }
            }
            await SendAdminAsync(chatId, "Створення промокоду скасовано.", cancellationToken);
            return;
        }

        if (data.StartsWith("payout_ok:", StringComparison.OrdinalIgnoreCase)
            || data.StartsWith("payout_no:", StringComparison.OrdinalIgnoreCase))
        {
            var approve = data.StartsWith("payout_ok:", StringComparison.OrdinalIgnoreCase);
            var rawId = data[(data.IndexOf(':') + 1)..];
            if (!Guid.TryParse(rawId, out var payoutId))
            {
                await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Некоректний ID заявки.", cancellationToken);
                return;
            }

            try
            {
                var updated = await referralService.ProcessPayoutRequestAsync(
                    payoutId,
                    new UpdatePayoutStatusRequest
                    {
                        Status = approve ? PayoutRequestStatus.Completed : PayoutRequestStatus.Rejected,
                        AdminComment = approve ? "Підтверджено адміном у Telegram" : "Відхилено адміном у Telegram"
                    },
                    cancellationToken);

                await telegramBotClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    approve ? "✅ Виплату підтверджено!" : "❌ Заявку відхилено.",
                    cancellationToken);

                if (callbackQuery.Message?.MessageId is { } msgId)
                {
                    var updatedText = (callbackQuery.Message.Text ?? string.Empty)
                        + "\n\n"
                        + (approve
                            ? "✅ <b>Заявку підтверджено та позначено як виплачену.</b>"
                            : "❌ <b>Заявку відхилено (кошти повернуто на баланс).</b>");

                    await telegramBotClient.EditMessageTextAsync(
                        chatId,
                        msgId,
                        updatedText,
                        inlineMarkup: null,
                        cancellationToken);
                }

                var statusMsg = approve
                    ? $"✅ Виплату <b>{Money(updated.Amount)} грн</b> для <b>{Html(updated.PartnerName)}</b> (<code>{Html(updated.PartnerReferralCode)}</code>) позначено як <b>ВИПЛАЧЕНО</b>."
                    : $"❌ Заявку на <b>{Money(updated.Amount)} грн</b> для <b>{Html(updated.PartnerName)}</b> (<code>{Html(updated.PartnerReferralCode)}</code>) <b>ВІДХИЛЕНО</b> (кошти повернуто на баланс реферала).";

                await SendAdminAsync(chatId, statusMsg, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Не вдалося обробити callback виплати {PayoutId}.", payoutId);
                await telegramBotClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    $"⚠️ {ex.Message}",
                    cancellationToken);
            }

            return;
        }

        if (data.StartsWith("promo_off:", StringComparison.OrdinalIgnoreCase))
        {
            var rawId = data[(data.IndexOf(':') + 1)..];
            if (!Guid.TryParse(rawId, out var promoId))
            {
                await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Некоректний ID промокоду.", cancellationToken);
                return;
            }

            var deactivated = await referralService.DeactivatePromoCodeAsync(promoId, cancellationToken);
            await telegramBotClient.AnswerCallbackQueryAsync(
                callbackQuery.Id,
                deactivated ? "🛑 Промокод вимкнено." : "Промокод не знайдено.",
                cancellationToken);

            if (deactivated)
            {
                await SendAdminAsync(chatId, "🛑 Промокод успішно вимкнено.", cancellationToken);
            }

            return;
        }

        if (data.StartsWith("ord_list:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = data.Split(':');
            var filter = parts.Length > 1 ? parts[1] : "all";
            var page = parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPage)
                ? parsedPage
                : 0;

            await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, null, cancellationToken);
            await SendOrEditAdminOrdersListAsync(
                chatId,
                callbackQuery.Message?.MessageId,
                filter,
                page,
                cancellationToken);
            return;
        }

        if (data.StartsWith("ord_view:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = data.Split(':');
            if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderNumber))
            {
                await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Некоректний номер замовлення.", cancellationToken);
                return;
            }

            var filter = parts.Length > 2 ? parts[2] : "all";
            var page = parts.Length > 3 && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPage)
                ? parsedPage
                : 0;

            await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, null, cancellationToken);
            await SendOrEditAdminOrderDetailAsync(
                chatId,
                callbackQuery.Message?.MessageId,
                orderNumber,
                filter,
                page,
                cancellationToken);
            return;
        }

        if (data.StartsWith("partner_pct:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = data.Split(':');
            if (parts.Length < 3
                || !Guid.TryParse(parts[1], out var partnerId)
                || !decimal.TryParse(parts[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var pct)
                || pct is <= 0m or > 100m)
            {
                await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Некоректні параметри.", cancellationToken);
                return;
            }

            try
            {
                var updated = await referralService.UpdatePartnerAsync(
                    partnerId,
                    new UpdateReferralPartnerRequest { CommissionPercent = pct },
                    cancellationToken);

                await telegramBotClient.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    $"✅ Для {updated.Name} встановлено {Percent(updated.CommissionPercent)}%",
                    cancellationToken);

                if (updated.TelegramUserId > 0)
                {
                    var pctText = Percent(updated.CommissionPercent);
                    var emoji = updated.CommissionPercent >= 5m ? "🎉" : "ℹ️";
                    _ = telegramBotClient.SendMessageAsync(
                        updated.TelegramUserId,
                        $"{emoji} Вашу партнерську ставку оновлено до <b>{pctText}%</b>! Нові замовлення за вашим посиланням рахуватимуться за новою ставкою.",
                        CancellationToken.None);
                }

                await SendAdminReferralsAsync(chatId, cancellationToken, callbackQuery.Message?.MessageId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Помилка оновлення відсотка партнера {PartnerId}.", partnerId);
                await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, "Помилка оновлення.", cancellationToken);
            }
            return;
        }

        await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, null, cancellationToken);
    }

    private Task<bool> SendAdminAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        return telegramBotClient.SendMessageAsync(chatId, text, TelegramBotMenu.AdminReplyMarkup, cancellationToken);
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
        string? invitedByReferralCode,
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
                    Name = BuildPartnerName(user),
                    CommissionPercent = 3.0m,
                    InvitedByReferralCode = string.IsNullOrWhiteSpace(invitedByReferralCode) ? null : invitedByReferralCode
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
            ? "https://www.jingledecor.com.ua"
            : configuredSiteUrl.Trim();

        return $"{siteUrl.TrimEnd('/')}/?ref={Uri.EscapeDataString(referralCode)}";
    }

    private string BuildBotInviteLink(string referralCode)
    {
        var botUsername = configuration["Telegram:BotUsername"]?.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(botUsername))
        {
            botUsername = "ref_ecommerce_bot";
        }

        return $"https://t.me/{botUsername}?start=inv_{Uri.EscapeDataString(referralCode)}";
    }

    private static string? ExtractStartPayload(string text)
    {
        if (!text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
        {
            return null;
        }

        var payload = parts[1].Trim();
        if (payload.StartsWith("inv_", StringComparison.OrdinalIgnoreCase))
        {
            payload = payload[4..];
        }

        return payload.Length is >= 2 and <= 32 ? payload : null;
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
        var now = DateTime.UtcNow;

        if (!WithdrawalStates.IsEmpty)
        {
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

        if (!AdminPromoStates.IsEmpty)
        {
            foreach (var pair in AdminPromoStates)
            {
                if (pair.Value.IsExpired(now))
                {
                    AdminPromoStates.TryRemove(pair.Key, out _);
                }
            }

            if (AdminPromoStates.Count > MaxActiveWithdrawalStates)
            {
                var excessCount = AdminPromoStates.Count - MaxActiveWithdrawalStates;
                var oldestKeys = AdminPromoStates
                    .OrderBy(pair => pair.Value.UpdatedAtUtc)
                    .Take(excessCount)
                    .Select(pair => pair.Key)
                    .ToArray();

                foreach (var key in oldestKeys)
                {
                    AdminPromoStates.TryRemove(key, out _);
                }
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

    private static string FormatOrderStatusEmoji(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.New => "🆕",
            OrderStatus.Confirmed => "📋",
            OrderStatus.Packed => "📦",
            OrderStatus.Shipped => "🚚",
            OrderStatus.Delivered => "✅",
            OrderStatus.Cancelled => "❌",
            OrderStatus.Refused => "🚫",
            OrderStatus.Returned => "↩️",
            _ => "•"
        };
    }

    private static string FormatPaymentStatusBadge(PaymentMethod method, PaymentStatus paymentStatus) =>
        (method, paymentStatus) switch
        {
            (_, PaymentStatus.Paid) => "💳✅",
            (PaymentMethod.Online, PaymentStatus.Failed) => "💳❌",
            (_, PaymentStatus.Refunded) => "💳↩️",
            (PaymentMethod.Online, _) => "⏳💳",
            _ => "💵"
        };

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

    private sealed class AdminPromoCreationState
    {
        public string? Code { get; set; }
        public bool CodeStepCompleted { get; set; }
        public decimal? DiscountPercent { get; set; }
        public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

        public void Touch() => UpdatedAtUtc = DateTime.UtcNow;

        public bool IsExpired(DateTime nowUtc) => nowUtc - UpdatedAtUtc > WithdrawalStateTtl;
    }
}
