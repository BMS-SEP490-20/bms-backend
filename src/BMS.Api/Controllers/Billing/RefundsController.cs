using System.Security.Claims;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// Refunds of cancelled paid orders (Quy trình Đợt 3A Q3, provisional until UC55 is confirmed). Rules and audit live
/// in <see cref="IOrderRefundService"/>. Replaces the former placeholder POST that answered "success" without doing anything.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// WHY: approve/reject/exception = Manager; counter pay-out = Receptionist; list = both. Admin, Lab, Doctor, Patient 403.
[Authorize]
public class RefundsController(IOrderRefundService refunds) : ControllerBase
{
    /// <summary>Refunds, newest first (optional <c>status</c>).</summary>
    [Authorize(Roles = RoleSets.ReceptionOrManager)]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) =>
        Ok(new { success = true, data = await refunds.ListAsync(status, ct) });

    /// <summary>One refund by id or RF- code.</summary>
    [Authorize(Roles = RoleSets.ReceptionOrManager)]
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct) =>
        Ok(new { success = true, data = await refunds.GetAsync(id, ct) });

    /// <summary>Manager exception: refund request for a cancelled paid order (e.g. cancelled after collection). Reason required.</summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> OpenException([FromBody] OpenRefundRequestDto request, CancellationToken ct) =>
        Ok(new { success = true, data = await refunds.OpenManagerExceptionAsync(request, CurrentActor(RoleNames.Manager), ct) });

    /// <summary>Manager approval (amount and channel fixed from the Paid invoice; BHYT part reversed only).</summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id, [FromBody] RefundDecisionRequestDto? request, CancellationToken ct) =>
        Ok(new { success = true, data = await refunds.ApproveAsync(id, request ?? new RefundDecisionRequestDto(), CurrentActor(RoleNames.Manager), ct) });

    /// <summary>Manager rejection (note required).</summary>
    [Authorize(Roles = RoleNames.Manager)]
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(string id, [FromBody] RefundDecisionRequestDto request, CancellationToken ct) =>
        Ok(new { success = true, data = await refunds.RejectAsync(id, request, CurrentActor(RoleNames.Manager), ct) });

    /// <summary>Receptionist returns an approved cash/POS refund at the counter.</summary>
    [Authorize(Roles = RoleNames.Receptionist)]
    [HttpPost("{id}/payout")]
    public async Task<IActionResult> PayOut(string id, CancellationToken ct) =>
        Ok(new { success = true, data = await refunds.PayOutAtCounterAsync(id, CurrentActor(RoleNames.Receptionist), ct) });

    // WHY from the token only: audit must name the real actor (ISO 15189).
    private Actor CurrentActor(string role) => new(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty,
        User.FindFirstValue(ClaimTypes.Name), role);
}
