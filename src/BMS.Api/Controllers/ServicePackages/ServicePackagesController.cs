using BMS.Core.Constants;
using System.Security.Claims;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.ServicePackages;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.ServicePackages;

[ApiController]
[Route("api/service-packages")]
[Route("api/v1/service-packages")]
[Produces("application/json")]
// WHY: UC15/UC19: public catalog read. Create/edit/delete = Admin + Manager: UC19 (test packages) has no
// separate controller and is managed through this one, and Quy trình ruled UC19 = Admin + Manager
// (DOT2 follow-up 2, C5 default, pending Phát). TODO(phat, UC19): narrow again if Phát splits UC15/UC19.
[Authorize]
public sealed class ServicePackagesController(
    IServicePackageService packages) : ControllerBase
{
    /// <summary>Xem danh sách gói dịch vụ (phân trang).</summary>
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ServicePackageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ServicePackageDto>>> List(
        [FromQuery] ServicePackageListRequest request, CancellationToken ct)
        => Ok(await packages.ListAsync(request, CanManage(), ct));

    /// <summary>Tìm kiếm gói dịch vụ theo từ khoá (mã/tên/mô tả/xét nghiệm trong gói) và khoảng giá.</summary>
    [AllowAnonymous]
    [HttpGet("search")]
    [ProducesResponseType(typeof(PagedResult<ServicePackageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ServicePackageDto>>> Search(
        [FromQuery] ServicePackageSearchRequest request, CancellationToken ct)
        => Ok(await packages.SearchAsync(request, CanManage(), ct));

    /// <summary>Xem chi tiết một gói dịch vụ.</summary>
    [AllowAnonymous]
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ServicePackageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServicePackageDto>> GetById(string id, CancellationToken ct)
        => Ok(await packages.GetByIdAsync(id, CanManage(), ct));

    /// <summary>Tạo gói dịch vụ mới.</summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpPost]
    [ProducesResponseType(typeof(ServicePackageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServicePackageDto>> Create(
        [FromBody] CreateServicePackageRequest request, CancellationToken ct)
    {
        var created = await packages.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Cập nhật gói dịch vụ (ghi đè toàn bộ trừ mã gói).</summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ServicePackageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServicePackageDto>> Update(
        string id, [FromBody] UpdateServicePackageRequest request, CancellationToken ct)
        => Ok(await packages.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>Xoá gói dịch vụ (xoá mềm – dữ liệu cũ vẫn được giữ cho hoá đơn/lịch sử).</summary>
    [Authorize(Roles = RoleSets.AdminOrManager)]
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await packages.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
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
