using BMS.Api.Authorization;
using BMS.Core.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Appointment;
using BMS.Application.DTOs.Clinic;
using BMS.Application.DTOs.MedicalRecords;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BMS.Api.Controllers.Clinic;

/// <summary>
/// Controller managing clinical medical assessments (UC50 / Add Medical Assessment - Iteration 3).
/// Conforms to: Production Standards, BR-DOC-01 (Mandatory ICD-10), and AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/medical-assessments")]
// WHY: UC50: Doctor writes; Doctor, Manager (view), Patient (own) read. Admin denied (decision 10). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class MedicalAssessmentsController : ControllerBase
{
    private readonly IMedicalAssessmentService _service;
    private readonly ILogger<MedicalAssessmentsController> _logger;

    public MedicalAssessmentsController(
        IMedicalAssessmentService service,
        ILogger<MedicalAssessmentsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new clinical medical assessment (UC50).
    /// </summary>
    [Authorize(Roles = RoleNames.Doctor)]
    [HttpPost]
    [ProducesResponseType(typeof(MedicalAssessmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateMedicalAssessmentDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Thông tin đánh giá y khoa không hợp lệ.",
                Errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray())
            });
        }

        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "USR-DOC-01";
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("fullName") ?? "Bác sĩ lâm sàng";

            var created = await _service.CreateAsync(dto, userId, userName, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, new
            {
                success = true,
                message = "Tạo đánh giá y khoa thành công.",
                data = created
            });
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
    /// Searches and filters medical assessments with pagination.
    /// </summary>
    [Authorize(Roles = RoleSets.DoctorManagerOrPatient)]
    [HttpGet]
    [ProducesResponseType(typeof(List<MedicalAssessmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Query([FromQuery] MedicalAssessmentFilterDto filter, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");

        var (items, total) = await _service.QueryAsync(filter, currentUserId, role, ct);
        return Ok(new
        {
            success = true,
            message = "Tra cứu danh sách đánh giá y khoa thành công.",
            total,
            page = filter.PageNumber,
            pageSize = filter.PageSize,
            data = items
        });
    }

    /// <summary>
    /// Gets details of a medical assessment by ID or Code.
    /// </summary>
    [Authorize(Roles = RoleSets.DoctorManagerOrPatient)]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(MedicalAssessmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");

        var item = await _service.GetByIdAsync(id, currentUserId, role, ct);
        if (item == null)
        {
            return NotFound(new ApiErrorResponse
            {
                Code = "MSG-ERR-EMR-01",
                Message = $"Không tìm thấy hồ sơ đánh giá y khoa với mã '{id}'."
            });
        }

        return Ok(new
        {
            success = true,
            data = item
        });
    }

    /// <summary>
    /// Gets all medical assessments for a specific patient.
    /// </summary>
    [Authorize(Roles = RoleSets.DoctorManagerOrPatient)]
    [HttpGet("patient/{patientId}")]
    [ProducesResponseType(typeof(List<MedicalAssessmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByPatient(string patientId, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var role = User.FindFirstValue(ClaimTypes.Role) ?? User.FindFirstValue("role");

        var list = await _service.GetByPatientIdAsync(patientId, currentUserId, role, ct);
        return Ok(new
        {
            success = true,
            patientId,
            total = list.Count,
            data = list
        });
    }

    /// <summary>
    /// Retrieves active encounters or appointments of a patient for clinical association.
    /// </summary>
    [Authorize(Roles = RoleSets.DoctorManagerOrPatient)]
    [HttpGet("patient/{patientId}/encounters")]
    [ProducesResponseType(typeof(List<PatientEncounterDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEncounters(string patientId, CancellationToken ct)
    {
        // Unlike the other reads, this service call has no viewer argument, so the Patient "own" rule is applied here.
        if (!await this.MayAccessPatientAsync(patientId, ct)) return Forbid();
        var encounters = await _service.GetPatientEncountersAsync(patientId, ct);
        return Ok(new
        {
            success = true,
            patientId,
            data = encounters
        });
    }

    /// <summary>
    /// Updates an existing draft medical assessment.
    /// </summary>
    [Authorize(Roles = RoleNames.Doctor)]
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(MedicalAssessmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateMedicalAssessmentDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Dữ liệu cập nhật đánh giá y khoa không hợp lệ.",
                Errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray())
            });
        }

        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "USR-DOC-01";
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("fullName");

            var updated = await _service.UpdateAsync(id, dto, userId, userName, ct);
            return Ok(new
            {
                success = true,
                message = "Cập nhật đánh giá y khoa thành công.",
                data = updated
            });
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
    /// Finalizes and locks a medical assessment (Doctor only).
    /// </summary>
    [Authorize(Roles = RoleNames.Doctor)]
    [HttpPatch("{id}/finalize")]
    [ProducesResponseType(typeof(MedicalAssessmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Finalize(string id, [FromBody] FinalizeMedicalAssessmentDto dto, CancellationToken ct)
    {
        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "USR-DOC-01";
            var userName = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("fullName");

            var finalized = await _service.FinalizeAsync(id, dto, userId, userName, ct);
            return Ok(new
            {
                success = true,
                message = "Đã hoàn tất và khóa hồ sơ đánh giá y khoa thành công.",
                data = finalized
            });
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
    /// Soft deletes a medical assessment with an audit reason.
    /// </summary>
    [Authorize(Roles = RoleNames.Doctor)]
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(string id, [FromBody] DeleteMedicalRecordRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Lý do hủy/xóa đánh giá y khoa không hợp lệ.",
                Errors = ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        k => k.Key,
                        v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray())
            });
        }

        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "USR-DOC-01";
            await _service.DeleteAsync(id, request.DeleteReason, userId, ct);

            return Ok(new
            {
                success = true,
                message = "Đã xóa mềm hồ sơ đánh giá y khoa thành công."
            });
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
