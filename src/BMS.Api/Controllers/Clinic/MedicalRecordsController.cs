using BMS.Application.DTOs.Appointment;
using BMS.Application.DTOs.MedicalRecords;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BMS.Api.Controllers.Clinic;

/// <summary>
/// UC33 – Medical Records (EMR) Management API.
/// Endpoints for Doctors to create and manage electronic medical records according to WHO ICD-10 standards.
/// </summary>
[ApiController]
[Route("api/medical-records")]
[Authorize]
public class MedicalRecordsController : ControllerBase
{
    private readonly IMedicalRecordService _service;
    private readonly ILogger<MedicalRecordsController> _logger;

    public MedicalRecordsController(
        IMedicalRecordService service,
        ILogger<MedicalRecordsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Gets all medical history records for a specific patient.
    /// </summary>
    [HttpGet("patient/{patientId}")]
    [ProducesResponseType(typeof(List<MedicalRecordDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByPatient(string patientId, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role") ?? string.Empty;

        var records = await _service.GetByPatientIdAsync(patientId, currentUserId, role, ct);
        return Ok(new
        {
            message = "Lấy lịch sử bệnh án thành công.",
            total = records.Count,
            data = records
        });
    }

    /// <summary>
    /// Gets details of a specific medical record by ID or RecordCode.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(MedicalRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role") ?? string.Empty;

        var record = await _service.GetByIdAsync(id, currentUserId, role, ct);
        if (record == null)
        {
            return NotFound(new ApiErrorResponse
            {
                Code = "MSG-ERR-EMR-01",
                Message = "Không tìm thấy hồ sơ bệnh án."
            });
        }

        return Ok(record);
    }

    /// <summary>
    /// Creates a new electronic medical record with ICD-10 and physiological validation (Doctor only).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Doctor,Admin,Manager")]
    [ProducesResponseType(typeof(MedicalRecordDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateMedicalRecordDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Thông tin bệnh án không hợp lệ.",
                Errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray())
            });
        }

        try
        {
            var doctorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "doctor";
            var created = await _service.CreateAsync(dto, doctorUserId, ct);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
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
    /// Updates an existing medical record within the legal 24-hour window (Doctor only).
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Doctor,Admin,Manager")]
    [ProducesResponseType(typeof(MedicalRecordDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateMedicalRecordDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Thông tin bệnh án cập nhật không hợp lệ.",
                Errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray())
            });
        }

        try
        {
            var doctorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "doctor";
            var updated = await _service.UpdateAsync(id, dto, doctorUserId, ct);

            return Ok(updated);
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
    /// Soft deletes a medical record with an audit justification reason (Doctor/Admin only).
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Doctor,Admin,Manager")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(string id, [FromBody] DeleteMedicalRecordRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Lý do hủy/xóa bệnh án không hợp lệ.",
                Errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray())
            });
        }

        try
        {
            var doctorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "doctor";
            await _service.DeleteAsync(id, request, doctorUserId, ct);

            return NoContent();
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

    private static string ExtractCode(string message)
    {
        var idx = message.IndexOf(':');
        return idx > 0 ? message[..idx].Trim() : "MSG-ERR-EMR-00";
    }
}
