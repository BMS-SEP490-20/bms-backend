using BMS.Core.Constants;
using BMS.Core.Exceptions;
using System.Security.Claims;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;
using BMS.Application.DTOs.Users;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// API Controller managing Specimen History retrieval, event logging and physical cold storage archive (UC70 / BR-212).
/// Complies with: ISO 15189:2022, Decision 2429/QD-BYT and BR-PHL-01 to BR-PHL-04.
/// </summary>
[ApiController]
[Route("api/v1/laboratory/specimens")]
[Route("api/laboratory/specimens")]
[Route("api/specimens")]
[Produces("application/json")]
// WHY: UC70: view Lab, Manager, Doctor; events/storage Lab. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize]
public class SpecimenHistoryController : ControllerBase
{
    private readonly ISpecimenHistoryService _historyService;
    private readonly ILogger<SpecimenHistoryController> _logger;

    public SpecimenHistoryController(
        ISpecimenHistoryService historyService,
        ILogger<SpecimenHistoryController> logger)
    {
        _historyService = historyService;
        _logger = logger;
    }

    /// <summary>
    /// Truy vấn danh sách lịch sử bệnh phẩm có phân trang và bộ lọc (UC70).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet("history")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SpecimenHistoryListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSpecimenHistory(
        [FromQuery] SpecimenHistoryQueryParameters query,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _historyService.GetSpecimenHistoryPagedAsync(query, ct);
            return Ok(ApiResponse<PagedResult<SpecimenHistoryListItemDto>>.Ok(
                result,
                "Danh sách lịch sử bệnh phẩm đã tải thành công."));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.Code));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail("Lỗi hệ thống khi tải danh sách lịch sử bệnh phẩm.", "MSG-ERR-INTERNAL"));
        }
    }

    /// <summary>
    /// Lấy chi tiết lịch sử mẫu bệnh phẩm theo mã vạch Barcode hoặc ID (UC70).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet("{barcode}/history")]
    [ProducesResponseType(typeof(ApiResponse<SpecimenHistoryDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSpecimenByBarcode(
        string barcode,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _historyService.GetSpecimenHistoryByBarcodeAsync(barcode, ct);
            if (result == null)
            {
                return NotFound(ApiResponse<object>.Fail(
                    $"Không tìm thấy dữ liệu lịch sử của mẫu bệnh phẩm '{barcode}'.",
                    "MSG-ERR-SPECIMEN-NOT-FOUND"));
            }

            return Ok(ApiResponse<SpecimenHistoryDetailDto>.Ok(
                result,
                $"Tải lịch sử mẫu bệnh phẩm {result.Barcode} thành công."));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.Code));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail("Lỗi hệ thống khi tải chi tiết lịch sử mẫu bệnh phẩm.", "MSG-ERR-INTERNAL"));
        }
    }

    /// <summary>
    /// Lấy danh sách lịch sử tất cả các mẫu bệnh phẩm của một bệnh nhân (UC70).
    /// </summary>
    [Authorize(Roles = RoleSets.ClinicalStaff)]
    [HttpGet("patient/{patientId}/history")]
    [ProducesResponseType(typeof(ApiResponse<List<SpecimenHistoryListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPatientSpecimens(
        string patientId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _historyService.GetSpecimensByPatientIdAsync(patientId, ct);
            return Ok(ApiResponse<List<SpecimenHistoryListItemDto>>.Ok(
                result,
                $"Tải danh sách mẫu bệnh phẩm của bệnh nhân {patientId} thành công."));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.Code));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail("Lỗi hệ thống khi tải lịch sử mẫu bệnh phẩm của bệnh nhân.", "MSG-ERR-INTERNAL"));
        }
    }

    /// <summary>
    /// Ghi nhận bổ sung một sự kiện theo dõi mẫu (Collection, InTransit, Received, Processing, MachineAnalysis) (UC70).
    /// </summary>
    [Authorize(Roles = RoleNames.Lab)]
    [HttpPost("{barcode}/events")]
    [ProducesResponseType(typeof(ApiResponse<SpecimenHistoryDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddTrackingEvent(
        string barcode,
        [FromBody] AddSpecimenTrackingEventDto eventDto,
        CancellationToken ct = default)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<object>.Fail("Dữ liệu sự kiện không hợp lệ.", "MSG-ERR-VALIDATION"));
            }

            var operatorId = CurrentUserId ?? "STAFF";
            var operatorName = CurrentUserName ?? eventDto.OperatorName ?? "Nhân viên y tế";

            _logger.LogInformation("[SpecimenHistory API] Adding tracking event {Event} to specimen {Barcode} by {User}",
                eventDto.EventType, barcode, operatorName);

            var (success, message, data) = await _historyService.AddTrackingEventAsync(
                barcode, eventDto, operatorId, operatorName, ct);

            if (!success)
            {
                if (message.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase))
                {
                    return NotFound(ApiResponse<object>.Fail(message, "MSG-ERR-SPECIMEN-NOT-FOUND"));
                }
                return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-EVENT-FAILED"));
            }

            return Ok(ApiResponse<SpecimenHistoryDetailDto>.Ok(data!, message));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.Code));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail("Lỗi hệ thống khi ghi nhận sự kiện theo dõi mẫu bệnh phẩm.", "MSG-ERR-INTERNAL"));
        }
    }

    /// <summary>
    /// Cập nhật vị trí lưu trữ mẫu, tủ bảo quản lạnh, kệ, khay và thông số bảo quản (UC70).
    /// </summary>
    [Authorize(Roles = RoleNames.Lab)]
    [HttpPut("{barcode}/storage")]
    [ProducesResponseType(typeof(ApiResponse<SpecimenHistoryDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStorageLocation(
        string barcode,
        [FromBody] UpdateSpecimenStorageDto storageDto,
        CancellationToken ct = default)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ApiResponse<object>.Fail("Dữ liệu lưu trữ không hợp lệ.", "MSG-ERR-VALIDATION"));
            }

            var operatorName = CurrentUserName ?? "KTV Xét Nghiệm";

            _logger.LogInformation("[SpecimenHistory API] Updating storage location for specimen {Barcode} to {Unit} ({Rack})",
                barcode, storageDto.StorageUnit, storageDto.RackId);

            var (success, message, data) = await _historyService.UpdateStorageAsync(
                barcode, storageDto, operatorName, ct);

            if (!success)
            {
                if (message.Contains("Không tìm thấy", StringComparison.OrdinalIgnoreCase))
                {
                    return NotFound(ApiResponse<object>.Fail(message, "MSG-ERR-SPECIMEN-NOT-FOUND"));
                }
                return BadRequest(ApiResponse<object>.Fail(message, "MSG-ERR-STORAGE-UPDATE-FAILED"));
            }

            return Ok(ApiResponse<SpecimenHistoryDetailDto>.Ok(data!, message));
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, ApiResponse<object>.Fail(ex.Message, ex.Code));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                ApiResponse<object>.Fail("Lỗi hệ thống khi cập nhật vị trí lưu trữ mẫu bệnh phẩm.", "MSG-ERR-INTERNAL"));
        }
    }

    private string? CurrentUserId =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    private string? CurrentUserName =>
        User.FindFirstValue("name") ?? User.FindFirstValue("fullName") ?? User.FindFirstValue(ClaimTypes.Name);
}
