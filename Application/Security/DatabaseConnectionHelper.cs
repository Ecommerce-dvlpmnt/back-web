using Npgsql;

namespace EcommerceBackend.Application.Security;

/// <summary>
/// Хелпер для безопасного получения и парсинга строки подключения к базе данных из конфигурации или переменных окружения.
/// Поддерживает как стандартный формат Npgsql, так и формат URI облачных провайдеров (DATABASE_URL=postgres://...).
/// </summary>
public static class DatabaseConnectionHelper
{
    public static string ResolveConnectionString(IConfiguration configuration)
    {
        // 1. Проверяем ConnectionStrings:Default в конфигурации (включая ConnectionStrings__Default в env vars)
        var connectionString = configuration.GetConnectionString("Default");

        // 2. Если не найдено, проверяем переменную DATABASE_URL (стандарт для Supabase, Render, Railway, Docker, Heroku)
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration["DATABASE_URL"] ?? Environment.GetEnvironmentVariable("DATABASE_URL");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Рядок підключення до бази даних не знайдено. " +
                "Вкажіть 'ConnectionStrings:Default' в appsettings або задайте змінну оточення " +
                "'ConnectionStrings__Default' або 'DATABASE_URL'.");
        }

        // Если передан URI вида postgres://user:password@host:port/database, преобразуем в Npgsql-формат
        if (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertPostgresUriToConnectionString(connectionString);
        }

        return connectionString;
    }

    /// <summary>
    /// Парсит URI вида postgres://user:password@host:port/database?sslmode=require в строку Npgsql.
    /// </summary>
    public static string ConvertPostgresUriToConnectionString(string postgresUri)
    {
        var uri = new Uri(postgresUri);
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;
        var database = uri.AbsolutePath.TrimStart('/');

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            SslMode = SslMode.Require,
            Pooling = true
        };

        return builder.ConnectionString;
    }
}
