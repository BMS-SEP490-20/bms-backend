using System.Security.Claims;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// Configurable billing data (Quy trình Đợt 3A Q1/Q2). Thin controller; rules and audit in <see cref="IBillingSettingsService"/>.
/// </summary>
[ApiController]
[Route("api/billing/settings")]
// WHY: BHYT group catalog = Admin or Manager (Q1); priority discount = Manager only (Q2). Others 403.
[Authorize]
public class BillingSettingsController(IBillingSettingsService settings) : ControllerBase
{
    /// <summary>BHYT group-code catalog (active and inactive).</summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpGet("bhyt-groups")]
    public async Task<IActionResult> GetBhytGroups(CancellationToken ct) =>
        Ok(new { success = true, data = await settings.GetBhytGroupCodesAsync(ct) });

    /// <summary>Adds or updates a group code (route value trimmed + upper-cased; 400 BHYT_GROUP_CODE_INVALID).</summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpPut("bhyt-groups/{code}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertBhytGroup(string code, [FromBody] UpsertBhytGroupCodeRequestDto request, CancellationToken ct) =>
        Ok(new { success = true, data = await settings.UpsertBhytGroupCodeAsync(code, request, CurrentActor(), ct) });

    /// <summary>Priority-discount parameters (default 0%).</summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpGet("priority-discount")]
    public async Task<IActionResult> GetPriorityDiscount(CancellationToken ct) =>
        Ok(new { success = true, data = await settings.GetPriorityDiscountAsync(ct) });

    /// <summary>Sets the priority-discount parameters (Manager only; 400 PRIORITY_DISCOUNT_INVALID).</summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpPut("priority-discount")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetPriorityDiscount([FromBody] PriorityDiscountSettingsDto request, CancellationToken ct) =>
        Ok(new { success = true, data = await settings.SetPriorityDiscountAsync(request, CurrentActor(), ct) });

    // WHY from the token only: the audit row must name the real actor (ISO 15189 traceability).
    private Actor CurrentActor() => new(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty,
        User.FindFirstValue(ClaimTypes.Name),
        User.IsInRole(RoleNames.Manager) ? RoleNames.Manager : User.IsInRole(RoleNames.Admin) ? RoleNames.Admin : "Unknown");
}
