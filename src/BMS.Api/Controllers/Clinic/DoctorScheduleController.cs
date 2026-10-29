using BMS.Core.Constants;
using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Clinic;

/// <summary>
/// API controller for UC16 – View Doctor Schedule.
/// Allowed roles: Doctor, Manager, Administrator, Receptionist.
/// </summary>
[ApiController]
[Route("api/v1/schedules")]
[Route("api/schedules")]
[Produces("application/json")]
// WHY: UC16: Admin, Manager, Doctor, Receptionist. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleSets.DoctorScheduleViewers)]
public class DoctorScheduleController : ControllerBase
{
    private readonly IDoctorScheduleService _service;
    private readonly ILogger<DoctorScheduleController> _logger;

    public DoctorScheduleController(
        IDoctorScheduleService service,
        ILogger<DoctorScheduleController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the doctor schedule day-view grid.
    /// </summary>
    /// <remarks>
    /// UC16 – View Doctor Schedule (Backend).
    /// Roles: Doctor, Manager, Administrator, Receptionist.
    /// </remarks>
    /// <param name="date">Target date (yyyy-MM-dd). Defaults to today.</param>
    /// <param name="doctorId">Optional – show only this doctor.</param>
    /// <param name="search">Optional – filter by name or specialty.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Schedule grid returned.</response>
    [HttpGet("doctors")]
    [ProducesResponseType(typeof(DoctorScheduleResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DoctorScheduleResponseDto>> GetDoctorSchedule(
        [FromQuery] string? date = null,
        [FromQuery] string? doctorId = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        DateTime? parsed = null;
        if (!string.IsNullOrWhiteSpace(date) &&
            DateTime.TryParse(date, out var d))
        {
            parsed = d.Date;
        }

        _logger.LogInformation(
            "UC16 GetDoctorSchedule. Date={Date}, DoctorId={DoctorId}, Search={Search}",
            parsed, doctorId, search);

        var result = await _service.GetDayScheduleAsync(parsed, doctorId, search, ct);
        return Ok(result);
    }
}