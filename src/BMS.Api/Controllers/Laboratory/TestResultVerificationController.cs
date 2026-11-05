using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BMS.Api.Hubs;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing laboratory technical verification protocol (UC72 / Verify Test Results / Issue I3-073).
/// Enables Lab Managers / Technical Supervisors to inspect verification checklists, sign off verified results with SHA-256 digital signature,
/// trigger real-time SignalR notifications, or return to technician for correction.
/// </summary>
[ApiController]
[Route("api/laboratory/verification")]
// WHY: UC72: Lab (verifier), Manager. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleSets.LabOrManager)]
public class TestResultVerificationController : ControllerBase
{
    private readonly ITestResultVerificationService _verificationService;
    private readonly IHubContext<QueueHub>? _hubContext;

    public TestResultVerificationController(
        ITestResultVerificationService verificationService,
        IHubContext<QueueHub>? hubContext = null)
    {
        _verificationService = verificationService;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Gets the list of specimens pending technical verification.
    /// </summary>
    [HttpGet("queue")]
    public async Task<IActionResult> GetVerificationQueue()
    {
        var queue = await _verificationService.GetVerificationQueueAsync();
        return Ok(new
        {
            success = true,
            data = queue
        });
    }

    /// <summary>
    /// Gets full technical verification details and checklist for a specimen.
    /// </summary>
    [HttpGet("{specimenId}")]
    public async Task<IActionResult> GetVerificationDetails(string specimenId)
    {
        var details = await _verificationService.GetVerificationDetailsBySpecimenIdAsync(specimenId);
        if (details == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Verification record for specimen '{specimenId}' was not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = details
        });
    }

    /// <summary>
    /// Approves technical verification, locks results with SHA-256 signature, triggers SignalR notification, and forwards to doctor.
    /// </summary>
    [HttpPost("{specimenId}/verify")]
    public async Task<IActionResult> VerifyTestResult(string specimenId, [FromBody] VerifyTestResultRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Invalid verification request payload.",
                errors = ModelState
            });
        }

        var verifierName = User.FindFirst(ClaimTypes.Name)?.Value 
            ?? User.FindFirst("name")?.Value 
            ?? "BSCKII. Lê Lan Anh (Trưởng Khoa)";

        var result = await _verificationService.VerifyTestResultAsync(specimenId, request, verifierName);

        // Real-time SignalR notification broadcast for Doctor Console and Patient Portal
        if (_hubContext != null)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("TestResultVerified", new
                {
                    specimenId = result.SpecimenId,
                    orderId = result.OrderId,
                    patientName = result.PatientName,
                    patientId = result.PatientId,
                    verifiedBy = result.VerifiedBy,
                    digitalSignature = result.DigitalSignature,
                    status = "Verified",
                    timestamp = DateTime.UtcNow
                });

                await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
                {
                    type = "TestResultApproved",
                    title = $"[KẾT QUẢ ĐÃ DUYỆT] Mẫu {result.SpecimenId} - Đơn {result.OrderId}",
                    message = $"Trưởng Khoa {result.VerifiedBy} đã hoàn tất thẩm định và ký số kết quả xét nghiệm {result.PackageName} của bệnh nhân {result.PatientName}.",
                    referenceId = result.SpecimenId,
                    timestamp = DateTime.UtcNow
                });
            }
            catch
            {
                // Non-fatal if client broadcast fails
            }
        }

        return Ok(new
        {
            success = true,
            message = "Test result technically verified, cryptographically signed, and forwarded to doctor approval.",
            data = result
        });
    }

    /// <summary>
    /// Rejects technical verification and returns specimen to lab technician for correction.
    /// </summary>
    [HttpPost("{specimenId}/return")]
    public async Task<IActionResult> ReturnTestResult(string specimenId, [FromBody] ReturnTestResultRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Invalid return request payload.",
                errors = ModelState
            });
        }

        var verifierName = User.FindFirst(ClaimTypes.Name)?.Value 
            ?? User.FindFirst("name")?.Value 
            ?? "BSCKII. Lê Lan Anh (Trưởng Khoa)";

        var result = await _verificationService.ReturnTestResultAsync(specimenId, request, verifierName);
        return Ok(new
        {
            success = true,
            message = "Test result returned to laboratory technician for correction.",
            data = result
        });
    }
}
