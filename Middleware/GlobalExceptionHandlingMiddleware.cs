using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EcommerceBackend.Middleware;

public sealed class GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        // Клиент сам закрыл запрос: это не ошибка сервера, пробрасываем без логов.
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Если ответ уже начал отправляться, поменять статус нельзя.
            if (context.Response.HasStarted)
                throw;

            await WriteErrorResponseAsync(context, exception);
        }
    }

    private async Task WriteErrorResponseAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = Map(exception);
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled error on {Method} {Path}, traceId={TraceId}",
                context.Request.Method, context.Request.Path, traceId);
        }
        else
        {
            logger.LogWarning("Request failed with {StatusCode} on {Method} {Path}: {Message}, traceId={TraceId}",
                statusCode, context.Request.Method, context.Request.Path, message, traceId);
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new ErrorResponse(statusCode, message, traceId);
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }

    // Соответствие исключения и HTTP-кода.
    private static (int StatusCode, string Message) Map(Exception exception) => exception switch
    {
        ArgumentException => (StatusCodes.Status400BadRequest, exception.Message),
        KeyNotFoundException => (StatusCodes.Status404NotFound, exception.Message),
        InvalidOperationException => (StatusCodes.Status409Conflict, exception.Message),
        DbUpdateException { InnerException: PostgresException postgres } => MapPostgres(postgres),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
    };

    // Ошибки базы по SQLSTATE: 23505 дубль уникального поля, 23503 несуществующая связь.
    private static (int StatusCode, string Message) MapPostgres(PostgresException postgres) =>
        postgres.SqlState switch
        {
            "23505" => (StatusCodes.Status409Conflict, "A record with the same unique value already exists."),
            "23503" => (StatusCodes.Status400BadRequest, "A referenced record does not exist."),
            _ => (StatusCodes.Status500InternalServerError, "A database error occurred.")
        };
}
