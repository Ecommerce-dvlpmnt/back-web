using System.Text.Json.Serialization;

namespace EcommerceBackend.Application.Telegram;

public sealed record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessage? Message,
    [property: JsonPropertyName("callback_query")] TelegramCallbackQuery? CallbackQuery = null);

public sealed record TelegramCallbackQuery(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("from")] TelegramUser From,
    [property: JsonPropertyName("message")] TelegramMessage? Message,
    [property: JsonPropertyName("data")] string? Data);

public sealed record TelegramMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("from")] TelegramUser? From,
    [property: JsonPropertyName("chat")] TelegramChat? Chat,
    [property: JsonPropertyName("date")] long Date,
    [property: JsonPropertyName("text")] string? Text);

public sealed record TelegramUser(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("is_bot")] bool IsBot,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("language_code")] string? LanguageCode);

public sealed record TelegramChat(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("type")] string? Type);

public sealed record TelegramReplyMarkup(
    [property: JsonPropertyName("keyboard")] IReadOnlyList<IReadOnlyList<string>> Keyboard,
    [property: JsonPropertyName("resize_keyboard")] bool ResizeKeyboard = true,
    [property: JsonPropertyName("is_persistent")] bool IsPersistent = true,
    [property: JsonPropertyName("one_time_keyboard")] bool OneTimeKeyboard = false,
    [property: JsonPropertyName("input_field_placeholder")] string? InputFieldPlaceholder = null);

public sealed record TelegramInlineKeyboardButton(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("callback_data")] string CallbackData);

public sealed record TelegramInlineKeyboardMarkup(
    [property: JsonPropertyName("inline_keyboard")] IReadOnlyList<IReadOnlyList<TelegramInlineKeyboardButton>> InlineKeyboard);

public sealed record TelegramBotCommand(
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("description")] string Description);
