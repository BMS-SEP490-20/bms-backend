using System.Security.Claims;
using BMS.Application.DTOs.Billing;
using BMS.Application.Services;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>SePay bank-transfer checkout and incoming-payment webhook.</summary>
[ApiController]
[Route("api/payments/sepay")]
[Route("api/v1/payments/sepay")]
public sealed class SePayController(SePayPaymentService sePay) : ControllerBase
{
    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpGet("checkout/{appointmentId}")]
    public async Task<IActionResult> Checkout(string appointmentId, CancellationToken ct)
    {
        if (!sePay.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                success = false,
                message = "SePay chưa được cấu hình số tài khoản và mã ngân hàng trên máy chủ."
            });
        }

        var userId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var checkout = await sePay.GetCheckoutAsync(appointmentId, userId, User.IsInRole(RoleNames.Patient), ct);
        if (checkout == null)
            return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn." });
        if (checkout.Amount <= 0)
            return BadRequest(new { success = false, message = "Không xác định được số tiền của gói xét nghiệm." });

        return Ok(new { success = true, data = checkout });
    }

    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpGet("status/{appointmentId}")]
    public async Task<IActionResult> Status(string appointmentId, CancellationToken ct)
    {
        var userId = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        var checkout = await sePay.GetCheckoutAsync(appointmentId, userId, User.IsInRole(RoleNames.Patient), ct);
        if (checkout == null)
            return NotFound(new { success = false, message = "Không tìm thấy lịch hẹn." });

        return Ok(new
        {
            success = true,
            data = new
            {
                checkout.AppointmentId,
                checkout.AppointmentCode,
                checkout.Status,
                checkout.Paid
            }
        });
    }

    [AllowAnonymous]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook([FromBody] SePayWebhookPayload payload, CancellationToken ct)
    {
        if (!sePay.ApiKeyMatches(Request.Headers.Authorization.ToString()))
            return Unauthorized(new { success = false });

        await sePay.HandleWebhookAsync(payload, ct);
        return Ok(new { success = true });
    }
}
