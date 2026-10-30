using BMS.Core.Constants;
using System.Security.Claims;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.ServicePackages;
using BMS.Application.Interfaces;
using BMS.Core.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.ServicePackages;

[ApiController]
[Route("api/service-packages")]
[Route("api/v1/service-packages")]
[Produces("application/json")]
// WHY: UC15/UC19: public catalog read. Create/edit/delete = Admin + Manager: UC19 (test packages) has no
// separate controller and is managed through this one, and Quy trình ruled UC19 = Admin + Manager
// (DOT2 follow-up 2, C5 default, pending Phát).
[Authorize]
public sealed class ServicePackagesController(
    IServicePackageService packages,
    ILogger<ServicePackagesController> logger) : ControllerBase
{
    /// <summary>Xem danh sách gói dịch vụ (phân trang).</summary>
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ServicePackageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] ServicePackageListRequest request, CancellationToken ct)
    {
        try
        {
            var result = await packages.ListAsync(request, CanManage(), ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, message = ex.Message, title = ex.Message, code = ex.Code });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi tải danh sách gói dịch vụ");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Đã xảy ra lỗi hệ thống khi tải danh sách gói dịch vụ.",
                title = "Lỗi hệ thống.",
                code = "INTERNAL_ERROR"
            });
        }
    }

    /// <summary>Tìm kiếm gói dịch vụ theo từ khoá (mã/tên/mô tả/xét nghiệm trong gói) và khoảng giá.</summary>
    [AllowAnonymous]
    [HttpGet("search")]
    [ProducesResponseType(typeof(PagedResult<ServicePackageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] ServicePackageSearchRequest request, CancellationToken ct)
    {
        try
        {
            var result = await packages.SearchAsync(request, CanManage(), ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, message = ex.Message, title = ex.Message, code = ex.Code });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi tìm kiếm gói dịch vụ");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Đã xảy ra lỗi hệ thống khi tìm kiếm gói dịch vụ.",
                title = "Lỗi hệ thống.",
                code = "INTERNAL_ERROR"
            });
        }
    }

    /// <summary>Xem chi tiết một gói dịch vụ.</summary>
    [AllowAnonymous]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ServicePackageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest(new { success = false, message = "Mã định danh gói dịch vụ không hợp lệ.", title = "Mã không hợp lệ.", code = "VALIDATION_ERROR" });
            }

            var result = await packages.GetByIdAsync(id, CanManage(), ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, message = ex.Message, title = ex.Message, code = ex.Code });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi xem chi tiết gói dịch vụ {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Đã xảy ra lỗi hệ thống khi tải thông tin gói dịch vụ.",
                title = "Lỗi hệ thống.",
                code = "INTERNAL_ERROR"
            });
        }
    }

    /// <summary>Tạo gói dịch vụ mới.</summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpPost]
    [ProducesResponseType(typeof(ServicePackageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateServicePackageRequest request, CancellationToken ct)
    {
        try
        {
            if (request == null)
            {
                return BadRequest(new { success = false, message = "Dữ liệu gói dịch vụ không hợp lệ.", title = "Thông tin không đầy đủ.", code = "VALIDATION_ERROR" });
            }

            var created = await packages.CreateAsync(request, CurrentUserId, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, message = ex.Message, title = ex.Message, code = ex.Code });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi tạo gói dịch vụ {Code}", request?.Code);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Đã xảy ra lỗi hệ thống khi tạo gói dịch vụ.",
                title = "Lỗi hệ thống.",
                code = "INTERNAL_ERROR"
            });
        }
    }

    /// <summary>Cập nhật gói dịch vụ (ghi đè toàn bộ trừ mã gói).</summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ServicePackageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        string id, [FromBody] UpdateServicePackageRequest request, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id) || request == null)
            {
                return BadRequest(new { success = false, message = "Dữ liệu yêu cầu không hợp lệ.", title = "Thông tin không đầy đủ.", code = "VALIDATION_ERROR" });
            }

            var updated = await packages.UpdateAsync(id, request, CurrentUserId, ct);
            return Ok(updated);
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, message = ex.Message, title = ex.Message, code = ex.Code });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi cập nhật gói dịch vụ {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Đã xảy ra lỗi hệ thống khi cập nhật gói dịch vụ.",
                title = "Lỗi hệ thống.",
                code = "INTERNAL_ERROR"
            });
        }
    }

    /// <summary>Xoá gói dịch vụ (xoá mềm – dữ liệu cũ vẫn được giữ cho hoá đơn/lịch sử).</summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest(new { success = false, message = "Mã định danh gói dịch vụ không hợp lệ.", title = "Mã không hợp lệ.", code = "VALIDATION_ERROR" });
            }

            await packages.DeleteAsync(id, CurrentUserId, ct);
            return NoContent();
        }
        catch (AppException ex)
        {
            return StatusCode(ex.StatusCode, new { success = false, message = ex.Message, title = ex.Message, code = ex.Code });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Lỗi xóa gói dịch vụ {Id}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                message = "Đã xảy ra lỗi hệ thống khi xóa gói dịch vụ.",
                title = "Lỗi hệ thống.",
                code = "INTERNAL_ERROR"
            });
        }
    }

    private string CurrentUserId =>
        User.FindFirstValue("sub")
        ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new UnauthorizedAccessException();

    /// <summary>Người có quyền quản lý được thấy cả gói đang ẩn; người khác chỉ thấy gói đang hoạt động.</summary>
    /// <remarks>WHY: same role set as the write actions, so hidden packages are visible exactly to the people who can edit them.</remarks>
    private bool CanManage() =>
        User.Identity?.IsAuthenticated == true
        && (User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.Manager));
}
