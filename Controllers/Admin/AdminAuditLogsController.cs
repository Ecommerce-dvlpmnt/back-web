using EcommerceBackend.Application.DTOs.Admin;
using EcommerceBackend.Application.DTOs.Products;
using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers.Admin;

[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class AdminAuditLogsController(IAuditLogService auditLogService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AdminAuditLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AdminAuditLogDto>>> GetAll(
        [FromQuery] AuditLogQuery query,
        CancellationToken cancellationToken)
    {
        var response = await auditLogService.GetPagedAsync(query, cancellationToken);
        return Ok(response);
    }
}
