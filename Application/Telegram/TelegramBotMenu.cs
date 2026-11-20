using System.Globalization;

namespace EcommerceBackend.Application.Telegram;

public static class TelegramBotMenu
{
    public const string LinkButton = "Моє посилання";
    public const string StatsButton = "Статистика";
    public const string OrdersButton = "Замовлення";
    public const string WithdrawButton = "Вивести кошти";
    public const string PayoutsButton = "Виплати";

    public const string ConfirmSavedCardButton = "✅ Так, на цю";
    public const string EnterNewCardButton = "💳 Вказати іншу";
    public const string CancelButton = "❌ Скасувати";

    // Кнопки адмін-панелі всередині бота
    public const string AdminOrdersButton = "📦 Замовлення";
    public const string AdminReferralsButton = "🤝 Реферали";
    public const string AdminPayoutsButton = "💸 Виплати";
    public const string AdminPromosButton = "🎟 Промокоди";
    public const string AdminCreatePromoButton = "➕ Створити промо";

    public static readonly IReadOnlyList<IReadOnlyList<string>> Keyboard =
    [
        [LinkButton, StatsButton],
        [OrdersButton, WithdrawButton],
        [PayoutsButton]
    ];

    public static readonly IReadOnlyList<IReadOnlyList<string>> AdminKeyboard =
    [
        [AdminOrdersButton, AdminReferralsButton],
        [AdminPayoutsButton, AdminPromosButton],
        [AdminCreatePromoButton]
    ];

    public static readonly IReadOnlyList<IReadOnlyList<string>> SavedCardConfirmationKeyboard =
    [
        [ConfirmSavedCardButton, EnterNewCardButton],
        [CancelButton]
    ];

    public static TelegramReplyMarkup ReplyMarkup { get; } = new(
        Keyboard,
        InputFieldPlaceholder: "Оберіть дію");

    public static TelegramReplyMarkup AdminReplyMarkup { get; } = new(
        AdminKeyboard,
        InputFieldPlaceholder: "Адмін-панель: оберіть дію");

    public static TelegramReplyMarkup SavedCardConfirmationReplyMarkup { get; } = new(
        SavedCardConfirmationKeyboard,
        OneTimeKeyboard: true,
        InputFieldPlaceholder: "Оберіть картку або введіть 16 цифр");

    public static IReadOnlyList<TelegramBotCommand> Commands { get; } =
    [
        new("start", "Реєстрація та головне меню"),
        new("link", "Моє реферальне посилання"),
        new("stats", "Статистика"),
        new("orders", "Останні замовлення"),
        new("withdraw", "Вивести кошти"),
        new("payouts", "Історія виплат"),
        new("myid", "Дізнатися свій Telegram ID")
    ];

    public static string? ResolveCommand(string text)
    {
        return text.Trim() switch
        {
            LinkButton => "/link",
            StatsButton => "/stats",
            OrdersButton => "/orders",
            WithdrawButton => "/withdraw",
            PayoutsButton => "/payouts",
            CancelButton => "/cancel",
            _ => null
        };
    }

    public static string? ResolveAdminCommand(string text)
    {
        return text.Trim() switch
        {
            AdminOrdersButton => "/admin_orders",
            AdminReferralsButton => "/admin_referrals",
            AdminPayoutsButton => "/admin_payouts",
            AdminPromosButton => "/admin_promos",
            AdminCreatePromoButton => "/admin_create_promo",
            CancelButton => "/cancel",
            _ => null
        };
    }

    public static IReadOnlyList<long> GetAdminUserIds(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var ids = new HashSet<long>();

        var section = configuration.GetSection("Telegram:AdminUserIds");
        foreach (var child in section.GetChildren())
        {
            if (long.TryParse(child.Value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed != 0)
            {
                ids.Add(parsed);
            }
        }

        var rawList = configuration["Telegram:AdminUserIds"];
        if (!string.IsNullOrWhiteSpace(rawList))
        {
            foreach (var part in rawList.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed != 0)
                {
                    ids.Add(parsed);
                }
            }
        }

        return ids.ToArray();
    }

    public static bool IsAdmin(IConfiguration configuration, long telegramUserId)
    {
        if (telegramUserId <= 0)
        {
            return false;
        }

        return GetAdminUserIds(configuration).Contains(telegramUserId);
    }
}
