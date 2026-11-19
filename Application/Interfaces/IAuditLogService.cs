using EcommerceBackend.Application.DTOs.Admin;
using EcommerceBackend.Application.DTOs.Products;

namespace EcommerceBackend.Application.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(
        string action,
        string entityType,
        string entityId,
        object details,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AdminAuditLogDto>> GetPagedAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default);
}
