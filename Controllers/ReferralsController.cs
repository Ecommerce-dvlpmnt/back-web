using EcommerceBackend.Application.DTOs.Referrals;
using EcommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EcommerceBackend.Controllers;

[ApiController]
[Route("api/referrals")]
[Produces("application/json")]
public sealed class ReferralsController(IReferralService referralService) : ControllerBase
{
    [HttpGet("validate/{referralCode}")]
    [ProducesResponseType(typeof(ReferralCodeValidationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReferralCodeValidationResponse>> ValidateCode(
        string referralCode,
        CancellationToken cancellationToken)
    {
        var result = await referralService.ValidateCodeAsync(referralCode, cancellationToken);
        return Ok(result);
    }
}
