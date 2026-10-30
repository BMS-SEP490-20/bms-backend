using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System.Security.Claims;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing laboratory technical verification protocol (UC72 / Verify Test Results / Issue I3-073).
/// Enables Lab Managers / Technical Supervisors to inspect verification checklists, sign off verified results, or return to technician.
/// </summary>
[ApiController]
[Route("api/laboratory/verification")]
// WHY: UC72: Lab (verifier), Manager. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleSets.LabOrManager)]
public class TestResultVerificationController : ControllerBase
{
    private readonly ITestResultVerificationService _verificationService;

    public TestResultVerificationController(ITestResultVerificationService verificationService)
    {
        _verificationService = verificationService;
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
    /// Approves technical verification and sends specimen to doctor approval.
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
            ?? "Trần Minh Tuấn";

        var result = await _verificationService.VerifyTestResultAsync(specimenId, request, verifierName);
        return Ok(new
        {
            success = true,
            message = "Test result technically verified and forwarded to doctor approval.",
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
            ?? "Trần Minh Tuấn";

        var result = await _verificationService.ReturnTestResultAsync(specimenId, request, verifierName);
        return Ok(new
        {
            success = true,
            message = "Test result returned to laboratory technician for correction.",
            data = result
        });
    }
}
