using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers.Admin;

[ApiController]
[Route("api/admin/referrals")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public sealed class AdminReferralsController(IReferralService referralService) : ControllerBase
{
    [HttpGet("partners")]
    [ProducesResponseType(typeof(IReadOnlyList<PartnerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PartnerResponse>>> GetAllPartners(
        CancellationToken cancellationToken)
    {
        var partners = await referralService.GetAllPartnersAsync(cancellationToken);
        return Ok(partners);
    }

    [HttpGet("partners/{id:guid}")]
    [ProducesResponseType(typeof(PartnerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PartnerResponse>> GetPartnerById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var partner = await referralService.GetPartnerByIdAsync(id, cancellationToken);
        return partner is null ? NotFound() : Ok(partner);
    }

    [HttpPost("partners")]
    [ProducesResponseType(typeof(PartnerResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PartnerResponse>> CreatePartner(
        [FromBody] CreatePartnerRequest request,
        CancellationToken cancellationToken)
    {
        var created = await referralService.CreatePartnerAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetPartnerById), new { id = created.Id }, created);
    }
}
