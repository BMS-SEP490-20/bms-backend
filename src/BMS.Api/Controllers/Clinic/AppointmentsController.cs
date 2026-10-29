using BMS.Api.Authorization;
using BMS.Core.Constants;
using BMS.Application.DTOs.Appointment;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BMS.Api.Controllers.Clinic;

/// <summary>
/// UC21 – Book Appointment & UC13 – Manage Appointments API.
/// Endpoints used by Patient Web, Patient Mobile and Receptionist/Doctor Web.
/// </summary>
[ApiController]
[Route("api/appointments")]
// WHY: UC21/23/24: book LT, BN (own); view LT, QL, BS, BN (own); edit/cancel LT, QL, BN (own). Admin and Lab denied. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _service;
    private readonly ILogger<AppointmentsController> _logger;

    public AppointmentsController(IAppointmentService service, ILogger<AppointmentsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Gets all appointments with filtering and pagination support (UC23).
    /// </summary>
    [Authorize(Roles = RoleSets.AppointmentReaders)]
    [HttpGet]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] AppointmentFilterRequest filter,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Tham số bộ lọc không hợp lệ.",
                Errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray())
            });
        }

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? User.FindFirstValue("sub")
                            ?? string.Empty;
        var role = User.FindFirstValue(ClaimTypes.Role)
                   ?? User.FindFirstValue("role")
                   ?? string.Empty;

        var result = await _service.GetFilteredAsync(filter, currentUserId, role, ct);

        return Ok(new
        {
            message = "Lấy danh sách cuộc hẹn thành công.",
            page = result.Page,
            pageSize = result.PageSize,
            total = result.TotalItems,
            totalPages = result.TotalPages,
            data = result.Items
        });
    }

    /// <summary>
    /// Books a new appointment for a patient (UC21).
    /// </summary>
    /// <param name="request">Patient, doctor, date, slot and selected packages.</param>
    /// <returns>Created appointment with QR token for later check-in.</returns>
    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpPost]
    [ProducesResponseType(typeof(BookAppointmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Book(
        [FromBody] BookAppointmentRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Dữ liệu không hợp lệ.",
                Errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray())
            });
        }

        try
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                                ?? User.FindFirstValue("sub")
                                ?? "system";

            var result = await _service.BookAsync(request, currentUserId, ct);

            _logger.LogInformation(
                "Appointment booked. Code={Code}, Patient={PatientId}, Doctor={DoctorId}",
                result.AppointmentCode, result.PatientId, result.DoctorId);

            return CreatedAtAction(nameof(GetById), new { id = result.AppointmentId }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = ExtractCode(ex.Message),
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Creates an appointment via direct DTO (UC13 Admin/Receptionist manual entry).
    /// </summary>
    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpPost("create")]
    [ProducesResponseType(typeof(AppointmentReadDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] AppointmentCreateDto dto, CancellationToken ct)
    {
        var result = await _service.CreateAsync(dto, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.AppointmentId }, result);
    }

    /// <summary>
    /// Returns available time slots for a doctor on a given date.
    /// </summary>
    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpGet("slots")]
    [ProducesResponseType(typeof(AvailableSlotsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableSlots(
        [FromQuery] string doctorId,
        [FromQuery] DateTime date,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(doctorId))
            return BadRequest(new ApiErrorResponse { Code = "MSG-ERR-VAL-01", Message = "doctorId is required." });

        var result = await _service.GetAvailableSlotsAsync(doctorId, date, ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns doctors who have schedule on the given date (optional specialty filter).
    /// </summary>
    [Authorize(Roles = RoleSets.ReceptionOrPatient)]
    [HttpGet("doctors")]
    [ProducesResponseType(typeof(List<DoctorOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableDoctors(
        [FromQuery] DateTime date,
        [FromQuery] string? specialty,
        CancellationToken ct)
    {
        var result = await _service.GetAvailableDoctorsAsync(date, specialty, ct);
        return Ok(result);
    }

    /// <summary>
    /// Gets a single appointment by MongoDB id or AppointmentCode.
    /// </summary>
    [Authorize(Roles = RoleSets.AppointmentReaders)]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(BookAppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        if (result == null)
            return NotFound(new ApiErrorResponse { Code = "MSG-ERR-APPT-06", Message = "Không tìm thấy lịch hẹn." });

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
        if (string.Equals(role, "Patient", StringComparison.OrdinalIgnoreCase) && !string.Equals(result.PatientId, currentUserId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        return Ok(result);
    }

    /// <summary>
    /// Gets detailed appointment record by MongoDB id.
    /// </summary>
    [Authorize(Roles = RoleSets.AppointmentReaders)]
    [HttpGet("{id}/detail")]
    [ProducesResponseType(typeof(AppointmentReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDetailById(string id, CancellationToken ct)
    {
        var result = await _service.GetDetailByIdAsync(id, ct);
        if (result == null)
            return NotFound(new ApiErrorResponse { Code = "MSG-ERR-APPT-06", Message = "Không tìm thấy lịch hẹn." });

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");
        if (string.Equals(role, "Patient", StringComparison.OrdinalIgnoreCase) && !string.Equals(result.PatientId, currentUserId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        return Ok(result);
    }

    /// <summary>
    /// Updates appointment status, notes, or assignment (UC13).
    /// </summary>
    [Authorize(Roles = RoleSets.AppointmentEditors)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] AppointmentUpdateDto dto, CancellationToken ct)
    {
        if (!await PatientOwnsAppointmentAsync(id, ct))
            return NotFound(new ApiErrorResponse { Code = "MSG-ERR-APPT-06", Message = "Không tìm thấy lịch hẹn cần cập nhật." });
        var success = await _service.UpdateAsync(id, dto, ct);
        if (!success)
            return NotFound(new ApiErrorResponse { Code = "MSG-ERR-APPT-06", Message = "Không tìm thấy lịch hẹn cần cập nhật." });

        return Ok(new { message = "Cập nhật lịch hẹn thành công." });
    }

    /// <summary>
    /// Cancels or deletes an appointment (UC13).
    /// </summary>
    [Authorize(Roles = RoleSets.AppointmentEditors)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (!await PatientOwnsAppointmentAsync(id, ct))
            return NotFound(new ApiErrorResponse { Code = "MSG-ERR-APPT-06", Message = "Không tìm thấy lịch hẹn cần xóa." });
        var success = await _service.DeleteAsync(id, ct);
        if (!success)
            return NotFound(new ApiErrorResponse { Code = "MSG-ERR-APPT-06", Message = "Không tìm thấy lịch hẹn cần xóa." });

        return NoContent();
    }

    /// <summary>
    /// Gets all appointments for a specific patient.
    /// </summary>
    [Authorize(Roles = RoleSets.AppointmentReaders)]
    [HttpGet("patient/{patientId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByPatientId(
        string patientId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        if (!await this.MayAccessPatientAsync(patientId, ct)) return Forbid();
        var data = await _service.GetByPatientIdAsync(patientId, ct);
        var total = data.Count();
        var items = data.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new
        {
            message = "Lấy danh sách cuộc hẹn của bệnh nhân thành công.",
            page,
            pageSize,
            total,
            data = items
        });
    }

    private static string ExtractCode(string message)
    {
        var idx = message.IndexOf(':');
        return idx > 0 ? message[..idx].Trim() : "MSG-ERR-APPT-00";
    }

    /// <summary>
    /// Matrix UC24 "BN (own)": a Patient may change or cancel only their own appointment. Staff roles were already
    /// filtered by [Authorize]. Missing and foreign appointments both return false (same 404, anti-BOLA).
    /// </summary>
    private async Task<bool> PatientOwnsAppointmentAsync(string id, CancellationToken ct)
    {
        if (!User.IsInRole(RoleNames.Patient)) return true;
        var appointment = await _service.GetByIdAsync(id, ct);
        if (appointment == null) return false;
        return string.Equals(appointment.PatientId, User.AccountId(), StringComparison.OrdinalIgnoreCase)
            || await this.MayAccessPatientAsync(appointment.PatientId ?? string.Empty, ct);
    }
}
