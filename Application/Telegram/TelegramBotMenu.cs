namespace EcommerceBackend.Application.Telegram;

public static class TelegramBotMenu
{
    public const string LinkButton = "Моє посилання";
    public const string StatsButton = "Статистика";
    public const string OrdersButton = "Замовлення";
    public const string WithdrawButton = "Вивести кошти";
    public const string PayoutsButton = "Виплати";

    public static readonly IReadOnlyList<IReadOnlyList<string>> Keyboard =
    [
        [LinkButton, StatsButton],
        [OrdersButton, WithdrawButton],
        [PayoutsButton]
    ];

    public static TelegramReplyMarkup ReplyMarkup { get; } = new(
        Keyboard,
        InputFieldPlaceholder: "Оберіть дію");

    public static IReadOnlyList<TelegramBotCommand> Commands { get; } =
    [
        new("start", "Реєстрація та реферальне посилання"),
        new("link", "Моє реферальне посилання"),
        new("stats", "Статистика"),
        new("orders", "Останні замовлення"),
        new("withdraw", "Вивести кошти"),
        new("payouts", "Історія виплат")
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
            _ => null
        };
    }
}
