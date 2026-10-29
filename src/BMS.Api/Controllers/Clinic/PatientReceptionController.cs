using BMS.Core.Constants;
using BMS.Application.DTOs.Appointment;
using BMS.Application.DTOs.Reception;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BMS.Api.Controllers.Clinic;

/// <summary>
/// UC28 – Receive Patient & Smart Queue Management API.
/// Endpoints for Doctors, Phlebotomists, and Receptionists to call, recall, skip, and complete patients in queue.
/// </summary>
[ApiController]
[Route("api/reception")]
// WHY: UC28: Receptionist only; queue-status requires login (C3 default). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleNames.Receptionist)]
public class PatientReceptionController : ControllerBase
{
    private readonly IPatientReceptionService _receptionService;
    private readonly ILogger<PatientReceptionController> _logger;

    public PatientReceptionController(
        IPatientReceptionService receptionService,
        ILogger<PatientReceptionController> logger)
    {
        _receptionService = receptionService;
        _logger = logger;
    }

    /// <summary>
    /// Calls the next checked-in patient in queue into the specified consultation room (Atomic Concurrency).
    /// </summary>
    [HttpPost("call-next")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CallNext(
        [FromBody] CallNextPatientRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Dữ liệu yêu cầu không hợp lệ."
            });
        }

        try
        {
            var result = await _receptionService.CallNextPatientAsync(request, ct);
            if (result == null)
            {
                return Ok(new
                {
                    message = "Hàng đợi phòng khám hiện không có bệnh nhân chờ.",
                    data = (object?)null
                });
            }

            return Ok(new
            {
                message = $"Đã gọi thành công bệnh nhân {result.PatientName} ({result.QueueNumberFormatted}).",
                data = result
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
    /// Recalls the current patient in the consultation room (up to 3 times max).
    /// </summary>
    [HttpPost("recall")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Recall(
        [FromBody] RecallPatientRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Dữ liệu yêu cầu không hợp lệ."
            });
        }

        try
        {
            var result = await _receptionService.RecallPatientAsync(request, ct);
            return Ok(new
            {
                message = $"Đã phát lại loa gọi số cho bệnh nhân {result.PatientName} (Lần {result.RecallCount}).",
                data = result
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
    /// Skips a patient after 3 unanswered calls and marks status as NoShow.
    /// </summary>
    [HttpPost("skip")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Skip(
        [FromBody] SkipPatientRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Dữ liệu yêu cầu không hợp lệ."
            });
        }

        try
        {
            await _receptionService.SkipPatientAsync(request, ct);
            return Ok(new
            {
                message = "Đã đánh dấu bỏ qua lượt (No-show) thành công."
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
    /// Completes clinical consultation / blood collection for the patient.
    /// </summary>
    [HttpPost("complete")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Complete(
        [FromBody] CompletePatientRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new ApiErrorResponse
            {
                Code = "MSG-ERR-VAL-01",
                Message = "Dữ liệu yêu cầu không hợp lệ."
            });
        }

        try
        {
            await _receptionService.CompletePatientAsync(request, ct);
            return Ok(new
            {
                message = "Đã hoàn thành lượt khám / lấy mẫu thành công."
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
    /// Gets current queue waiting list and currently serving patient for clinic or kiosk boards.
    /// </summary>
    /// <remarks>
    /// Moved from GET api/reception/queue, which clashed with QueueController (UC27). That route still
    /// returns this payload when roomId/doctorId are supplied.
    /// </remarks>
    [HttpGet("queue-status")]
    [ProducesResponseType(typeof(QueueStatusResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQueue(
        [FromQuery] string? roomId,
        [FromQuery] string? doctorId,
        CancellationToken ct)
    {
        var result = await _receptionService.GetQueueStatusAsync(roomId, doctorId, ct);
        return Ok(result);
    }

    private static string ExtractCode(string message)
    {
        var idx = message.IndexOf(':');
        return idx > 0 ? message[..idx].Trim() : "MSG-ERR-RECV-00";
    }
}
