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
    /// Gets all appointments with pagination support.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var data = await _service.GetAllAsync(ct);
        var total = data.Count();
        var items = data.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new
        {
            message = "Lấy danh sách cuộc hẹn thành công.",
            page,
            pageSize,
            total,
            data = items
        });
    }

    /// <summary>
    /// Books a new appointment for a patient (UC21).
    /// </summary>
    /// <param name="request">Patient, doctor, date, slot and selected packages.</param>
    /// <returns>Created appointment with QR token for later check-in.</returns>
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
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(BookAppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        if (result == null)
            return NotFound(new ApiErrorResponse { Code = "MSG-ERR-APPT-06", Message = "Không tìm thấy lịch hẹn." });

        return Ok(result);
    }

    /// <summary>
    /// Gets detailed appointment record by MongoDB id.
    /// </summary>
    [HttpGet("{id}/detail")]
    [ProducesResponseType(typeof(AppointmentReadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetailById(string id, CancellationToken ct)
    {
        var result = await _service.GetDetailByIdAsync(id, ct);
        if (result == null)
            return NotFound(new ApiErrorResponse { Code = "MSG-ERR-APPT-06", Message = "Không tìm thấy lịch hẹn." });

        return Ok(result);
    }

    /// <summary>
    /// Updates appointment status, notes, or assignment (UC13).
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] AppointmentUpdateDto dto, CancellationToken ct)
    {
        var success = await _service.UpdateAsync(id, dto, ct);
        if (!success)
            return NotFound(new ApiErrorResponse { Code = "MSG-ERR-APPT-06", Message = "Không tìm thấy lịch hẹn cần cập nhật." });

        return Ok(new { message = "Cập nhật lịch hẹn thành công." });
    }

    /// <summary>
    /// Cancels or deletes an appointment (UC13).
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var success = await _service.DeleteAsync(id, ct);
        if (!success)
            return NotFound(new ApiErrorResponse { Code = "MSG-ERR-APPT-06", Message = "Không tìm thấy lịch hẹn cần xóa." });

        return NoContent();
    }

    /// <summary>
    /// Gets all appointments for a specific patient.
    /// </summary>
    [HttpGet("patient/{patientId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByPatientId(
        string patientId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
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
}
