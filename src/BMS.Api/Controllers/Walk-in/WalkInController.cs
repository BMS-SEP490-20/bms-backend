using BMS.Core.Constants;
using BMS.Core.Exceptions;
using System.Security.Claims;
using BMS.WalkIn.DTOs;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Collections.Generic;

namespace BMS.WalkIn.Controllers;

/// <summary>
/// UC26 – Register walk-in / guest patient at reception.
/// Thin API over PatientService.CreateGuestProfileAsync.
/// </summary>
[ApiController]
[Route("api/walk-in")]
// WHY: UC26: Receptionist only (Admin removed). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleNames.Receptionist)]
public class WalkInController : ControllerBase
{
    private readonly IWalkInService _service;
    private readonly ILogger<WalkInController> _logger;

    public WalkInController(IWalkInService service, ILogger<WalkInController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(RegisterWalkInResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterWalkInRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            var errorsDict = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    k => k.Key,
                    v => v.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );
            var errList = errorsDict.Values.SelectMany(x => x).Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            var detailMsg = string.Join("; ", errList);

            return BadRequest(new ApiErrorResponse
            {
                Code = "VALIDATION_ERROR",
                Message = !string.IsNullOrWhiteSpace(detailMsg) ? detailMsg : "Vui lòng điền đầy đủ các thông tin bắt buộc.",
                Errors = errorsDict
            });
        }

        try
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? User.FindFirstValue("sub");
            var result = await _service.RegisterAsync(request, userId, ct);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (PatientDuplicateException ex)
        {
            _logger.LogWarning("Đăng ký bệnh nhân vãng lai bị trùng: {Message} (Code: {Code})", ex.Message, ex.Code);
            return StatusCode(StatusCodes.Status409Conflict, new ApiErrorResponse
            {
                Code = ex.Code,
                Message = ex.Message,
                Errors = new Dictionary<string, string[]>
                {
                    ["IdentityNumber"] = new[] { ex.Message }
                }
            });
        }
        catch (DomainValidationException ex)
        {
            _logger.LogWarning("Dữ liệu đăng ký bệnh nhân vãng lai không hợp lệ: {Message} (Code: {Code})", ex.Message, ex.Code);
            return BadRequest(new ApiErrorResponse
            {
                Code = ex.Code,
                Message = ex.Message
            });
        }
        catch (AppException ex)
        {
            _logger.LogWarning("Lỗi nghiệp vụ khi đăng ký bệnh nhân vãng lai: {Message} (Code: {Code}, Status: {Status})", ex.Message, ex.Code, ex.StatusCode);
            return StatusCode((int)ex.StatusCode, new ApiErrorResponse
            {
                Code = ex.Code,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi không xác định khi đăng ký bệnh nhân vãng lai");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse
            {
                Code = "INTERNAL_ERROR",
                Message = "Đã xảy ra lỗi hệ thống khi tiếp đón bệnh nhân vãng lai. Vui lòng thử lại sau."
            });
        }
    }

    [HttpGet("lookup/identity/{identityNumber}")]
    [ProducesResponseType(typeof(PatientLookupResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PatientLookupResponse>> LookupIdentity(
        string identityNumber,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.LookupByIdentityAsync(identityNumber, ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tra cứu CCCD");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse { Code = "INTERNAL_ERROR", Message = "Lỗi hệ thống khi tra cứu CCCD." });
        }
    }

    [HttpGet("lookup/phone/{phone}")]
    [ProducesResponseType(typeof(PatientLookupResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<PatientLookupResponse>> LookupPhone(
        string phone,
        CancellationToken ct)
    {
        try
        {
            var result = await _service.LookupByPhoneAsync(phone, ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode((int)ex.StatusCode, new ApiErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tra cứu SĐT");
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse { Code = "INTERNAL_ERROR", Message = "Lỗi hệ thống khi tra cứu SĐT." });
        }
    }
}
