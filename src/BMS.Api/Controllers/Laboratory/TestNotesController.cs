using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller for clinical laboratory test notes management (UC52 / Add Test Notes).
/// Provides endpoints to attach observations, condition notes, and QC flags to specimens.
/// Conforms to ISO 15189 laboratory specifications.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// WHY: UC52: Lab, Doctor add; Manager view. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class TestNotesController : ControllerBase
{
    private readonly ITestNoteService _noteService;

    public TestNotesController(ITestNoteService noteService)
    {
        _noteService = noteService;
    }

    /// <summary>
    /// Retrieves specimen diagnostic context along with existing notes timeline (UC52).
    /// </summary>
    /// <param name="specimenId">The specimen barcode or specimen identifier.</param>
    [Authorize(Roles = RoleSets.TestNoteReaders)]
    [HttpGet("context/{specimenId}")]
    public async Task<IActionResult> GetSpecimenContext(string specimenId)
    {
        if (string.IsNullOrWhiteSpace(specimenId))
        {
            return BadRequest(new
            {
                success = false,
                message = "Specimen ID cannot be empty."
            });
        }

        var context = await _noteService.GetSpecimenContextAsync(specimenId);
        if (context == null)
        {
            return NotFound(new
            {
                success = false,
                message = $"Specimen '{specimenId}' not found in database."
            });
        }

        return Ok(new
        {
            success = true,
            data = context
        });
    }

    /// <summary>
    /// Retrieves all notes attached to a specific specimen.
    /// </summary>
    /// <param name="specimenId">The specimen barcode or identifier.</param>
    [Authorize(Roles = RoleSets.TestNoteReaders)]
    [HttpGet("specimen/{specimenId}")]
    public async Task<IActionResult> GetNotesBySpecimen(string specimenId)
    {
        if (string.IsNullOrWhiteSpace(specimenId))
        {
            return BadRequest(new
            {
                success = false,
                message = "Specimen ID cannot be empty."
            });
        }

        var notes = await _noteService.GetNotesBySpecimenAsync(specimenId);
        return Ok(new
        {
            success = true,
            data = notes
        });
    }

    /// <summary>
    /// Adds a new test note to a specimen (UC52).
    /// </summary>
    [Authorize(Roles = RoleSets.DoctorOrLab)]
    [HttpPost]
    public async Task<IActionResult> CreateNote([FromBody] CreateTestNoteRequestDto request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            // Extract authenticated user info from claims
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                         User.FindFirstValue("sub") ??
                         User.FindFirstValue("uid");
            var userName = User.FindFirstValue(ClaimTypes.Name) ??
                           User.FindFirstValue("name");
            var userRole = User.FindFirstValue(ClaimTypes.Role) ??
                           User.FindFirstValue("role") ??
                           "Lab Technician";

            var createdNote = await _noteService.CreateNoteAsync(request, userId, userName, userRole);

            return CreatedAtAction(
                nameof(GetNotesBySpecimen),
                new { specimenId = createdNote.SpecimenId },
                new
                {
                    success = true,
                    message = "Test note successfully added to specimen.",
                    data = createdNote
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while creating the test note: " + ex.Message
            });
        }
    }
}
