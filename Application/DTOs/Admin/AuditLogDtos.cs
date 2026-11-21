using System.Text.Json;

namespace EcommerceBackend.Application.DTOs.Admin;

public sealed record AdminAuditLogDto(
    Guid Id,
    string AdminUsername,
    string Action,
    string EntityType,
    string EntityId,
    JsonElement Details,
    DateTime CreatedAt);

public sealed record AuditLogQuery
{
    public string? AdminUsername { get; init; }
    public string? Action { get; init; }
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
