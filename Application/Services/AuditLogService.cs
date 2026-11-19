using System.Security.Claims;
using System.Text.Json;
using EcommerceBackend.Application.DTOs.Admin;
using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Application.Services;

public sealed class AuditLogService(
    AppDbContext dbContext,
    IHttpContextAccessor httpContextAccessor) : IAuditLogService
{
    public Task LogAsync(
        string action,
        string entityType,
        string entityId,
        object details,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);

        var httpContext = httpContextAccessor.HttpContext;
        var username = httpContext?.User.FindFirstValue(ClaimTypes.Name)
            ?? httpContext?.User.FindFirstValue("nickname");

        if (string.IsNullOrWhiteSpace(username))
            username = "system";

        username = username.Trim();
        if (username.Length > 30)
            username = username[..30];

        dbContext.AdminAuditLogs.Add(new AdminAuditLog
        {
            Id = Guid.NewGuid(),
            AdminUsername = username,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = JsonSerializer.Serialize(details),
            CreatedAt = DateTime.UtcNow
        });

        return Task.CompletedTask;
    }

    public async Task<PagedResult<AdminAuditLogDto>> GetPagedAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = query.PageNumber <= 0 ? 1 : query.PageNumber;
        var pageSize = query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, 100);
        var skip = (int)Math.Min((long)(pageNumber - 1) * pageSize, int.MaxValue);

        var dbQuery = dbContext.AdminAuditLogs
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.AdminUsername))
        {
            var adminUsername = query.AdminUsername.Trim();
            dbQuery = dbQuery.Where(log => log.AdminUsername == adminUsername);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            var action = query.Action.Trim();
            dbQuery = dbQuery.Where(log => log.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim();
            dbQuery = dbQuery.Where(log => log.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            var entityId = query.EntityId.Trim();
            dbQuery = dbQuery.Where(log => log.EntityId == entityId);
        }

        if (query.From.HasValue)
        {
            var fromUtc = query.From.Value.Kind == DateTimeKind.Utc
                ? query.From.Value
                : DateTime.SpecifyKind(query.From.Value, DateTimeKind.Utc);
            dbQuery = dbQuery.Where(log => log.CreatedAt >= fromUtc);
        }

        if (query.To.HasValue)
        {
            var toUtc = query.To.Value.Kind == DateTimeKind.Utc
                ? query.To.Value
                : DateTime.SpecifyKind(query.To.Value, DateTimeKind.Utc);
            dbQuery = dbQuery.Where(log => log.CreatedAt <= toUtc);
        }

        var totalCount = await dbQuery.CountAsync(cancellationToken);
        var logs = await dbQuery
            .OrderByDescending(log => log.CreatedAt)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AdminAuditLogDto>
        {
            Items = logs.Select(MapToDto).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static AdminAuditLogDto MapToDto(AdminAuditLog log)
    {
        JsonElement details;
        try
        {
            using var document = JsonDocument.Parse(log.Details);
            details = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            details = JsonSerializer.SerializeToElement(new { });
        }

        return new AdminAuditLogDto(
            log.Id,
            log.AdminUsername,
            log.Action,
            log.EntityType,
            log.EntityId,
            details,
            log.CreatedAt);
    }
}
