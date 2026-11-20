using System.Globalization;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Application.Telegram;

namespace EcommerceBackend.Infrastructure.Telegram;

public sealed class TelegramBotPollingService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelegramBotPollingService> logger) : BackgroundService
{
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Telegram:PollingEnabled"))
        {
            logger.LogInformation("Telegram long polling вимкнено.");
            return;
        }

        var botToken = configuration["Telegram:BotToken"]?.Trim();
        if (string.IsNullOrWhiteSpace(botToken))
        {
            logger.LogWarning("Telegram long polling не запущено: Telegram:BotToken не заданий.");
            return;
        }

        var timeoutSeconds = Math.Clamp(
            configuration.GetValue<int?>("Telegram:PollingTimeoutSeconds") ?? 30,
            1,
            50);

        try
        {
            using var scope = scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
            if (!await client.SetMyCommandsAsync(TelegramBotMenu.Commands, stoppingToken))
            {
                logger.LogWarning("Не вдалося налаштувати меню команд Telegram.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не вдалося налаштувати меню команд Telegram.");
        }

        var offset = await LoadOffsetAsync(stoppingToken);

        logger.LogInformation("Telegram long polling запущено з offset {Offset}.", offset);

        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<TelegramUpdate> updates;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
                updates = await client.GetUpdatesAsync(offset, timeoutSeconds, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Помилка отримання оновлень Telegram. Повтор через 5 секунд.");
                await DelayAfterErrorAsync(stoppingToken);
                continue;
            }

            foreach (var update in updates)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var handler = scope.ServiceProvider.GetRequiredService<TelegramBotCommandHandler>();
                    await handler.HandleUpdateAsync(update, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Помилка обробки Telegram update {UpdateId}.",
                        update.UpdateId);
                }

                offset = update.UpdateId + 1;
                await SaveOffsetAsync(offset, stoppingToken);
            }
        }
    }

    private async Task<long> LoadOffsetAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = GetOffsetFilePath();
            if (!File.Exists(path))
            {
                return 0;
            }

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            return long.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)
                ? Math.Max(0, offset)
                : 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не вдалося прочитати offset Telegram. Починаю з 0.");
            return 0;
        }
    }

    private async Task SaveOffsetAsync(long offset, CancellationToken cancellationToken)
    {
        try
        {
            var path = GetOffsetFilePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(
                path,
                offset.ToString(CultureInfo.InvariantCulture),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не вдалося зберегти offset Telegram {Offset}.", offset);
        }
    }

    private string GetOffsetFilePath()
    {
        var configuredPath = configuration["Telegram:OffsetFilePath"];
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath.Trim();
        }

        return Path.Combine(AppContext.BaseDirectory, "telegram-offset.txt");
    }

    private static async Task DelayAfterErrorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ErrorDelay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
