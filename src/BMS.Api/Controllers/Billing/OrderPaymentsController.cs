using System.Security.Claims;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// Payment gate for test orders (UC53/UC55, BA:L38). Amounts are computed by the server from the stored order.
/// </summary>
[ApiController]
[Route("api/billing/orders")]
// WHY: counter payment only Receptionist (QĐ10); Patient pays own order online via VNPay only (QĐ15); Manager 403.
[Authorize]
public class OrderPaymentsController(IOrderPaymentService payments) : ControllerBase
{
    /// <summary>Fee preview for an order.</summary>
    [Authorize(Roles = RoleNames.Receptionist)]
    [HttpPost("{orderId}/quote")]
    [ProducesResponseType(typeof(CalculateFeeResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Quote(string orderId, [FromBody] OrderPaymentRequestDto request, CancellationToken ct) =>
        Ok(new { success = true, data = await payments.QuoteAsync(orderId, request, ct) });

    /// <summary>Cash/POS at the counter → invoice Paid, order PaidPendingSample. 409 if already paid.</summary>
    [Authorize(Roles = RoleNames.Receptionist)]
    [HttpPost("{orderId}/pay")]
    [ProducesResponseType(typeof(OrderPaymentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PayAtCounter(string orderId, [FromBody] OrderPaymentRequestDto request, CancellationToken ct) =>
        Ok(new { success = true, data = await payments.PayAtCounterAsync(orderId, request, UserId(), User.FindFirstValue(ClaimTypes.Name), ct) });

    /// <summary>Starts VNPay (invoice Unpaid until a verified callback). Patient: own order and VNPay only, else 403.</summary>
    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpPost("{orderId}/vnpay")]
    [ProducesResponseType(typeof(OrderPaymentResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> StartVnPay(string orderId, [FromBody] OrderPaymentRequestDto request, CancellationToken ct) =>
        Ok(new { success = true, data = await payments.StartVnPayAsync(orderId, request, UserId(), User.IsInRole(RoleNames.Patient), ct) });

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
}
