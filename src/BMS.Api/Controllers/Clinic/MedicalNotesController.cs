using BMS.Core.Constants;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BMS.Application.DTOs.Clinic;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Clinic;

/// <summary>
/// UC51 supplementary notes for the authenticated treating doctor.
/// </summary>
[ApiController]
[Route("api/medical-notes")]
[Route("api/MedicalNotes")]
// WHY: UC51: matrix lists Doctor and Lab (SRS), but notes are authored/read per doctor (DoctorId from the
// token) and the UC51 check pins Roles == "Doctor". Kept Doctor-only until Quy trình decides how Lab notes work.
[Authorize(Roles = RoleNames.Doctor)]
public sealed class MedicalNotesController(IMedicalNoteService service) : ControllerBase
{
    private string? DoctorId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");

    /// <summary>
    /// Loads an existing EMR by ID/code and a paginated note history.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(MedicalNoteContextDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotes(
        [FromQuery, Required, StringLength(100)] string recordId,
        [FromQuery, Range(1, 10000)] int page = 1,
        [FromQuery, Range(1, 50)] int pageSize = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(DoctorId)) return Unauthorized();
        return Ok(await service.GetContextAsync(recordId, DoctorId, page, pageSize, ct));
    }

    /// <summary>Appends a note; retrying the same submission UUID returns the stored note.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(MedicalNoteDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> AddNote([FromBody] CreateMedicalNoteDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(DoctorId)) return Unauthorized();
        var authorName = User.FindFirstValue("fullName") ?? User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var note = await service.AddAsync(request, DoctorId, authorName, ct);
        return CreatedAtAction(nameof(GetNotes), new { recordId = note.RecordId }, note);
    }
}
