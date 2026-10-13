using System.Security.Claims;
using BMS.Api.Authorization;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.ServicePackages;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.ServicePackages;

[ApiController]
[Route("api/service-packages")]
[Produces("application/json")]
[Authorize]
public sealed class ServicePackagesController(
    IServicePackageService packages,
    IAuthorizationService authorization) : ControllerBase
{
    /// <summary>Xem danh sách gói dịch vụ (phân trang).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ServicePackageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ServicePackageDto>>> List(
        [FromQuery] ServicePackageListRequest request, CancellationToken ct)
        => Ok(await packages.ListAsync(request, await CanManageAsync(), ct));

    /// <summary>Tìm kiếm gói dịch vụ theo từ khoá (mã/tên/mô tả/xét nghiệm trong gói) và khoảng giá.</summary>
    [HttpGet("search")]
    [ProducesResponseType(typeof(PagedResult<ServicePackageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ServicePackageDto>>> Search(
        [FromQuery] ServicePackageSearchRequest request, CancellationToken ct)
        => Ok(await packages.SearchAsync(request, await CanManageAsync(), ct));

    /// <summary>Xem chi tiết một gói dịch vụ.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ServicePackageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServicePackageDto>> GetById(string id, CancellationToken ct)
        => Ok(await packages.GetByIdAsync(id, await CanManageAsync(), ct));

    /// <summary>Tạo gói dịch vụ mới.</summary>
    [HttpPost]
    [HasPermission(Permissions.ManageServicePackage)]
    [ProducesResponseType(typeof(ServicePackageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ServicePackageDto>> Create(
        [FromBody] CreateServicePackageRequest request, CancellationToken ct)
    {
        var created = await packages.CreateAsync(request, CurrentUserId, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Cập nhật gói dịch vụ (ghi đè toàn bộ trừ mã gói).</summary>
    [HttpPut("{id}")]
    [HasPermission(Permissions.ManageServicePackage)]
    [ProducesResponseType(typeof(ServicePackageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ServicePackageDto>> Update(
        string id, [FromBody] UpdateServicePackageRequest request, CancellationToken ct)
        => Ok(await packages.UpdateAsync(id, request, CurrentUserId, ct));

    /// <summary>Xoá gói dịch vụ (xoá mềm – dữ liệu cũ vẫn được giữ cho hoá đơn/lịch sử).</summary>
    [HttpDelete("{id}")]
    [HasPermission(Permissions.ManageServicePackage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        await packages.DeleteAsync(id, CurrentUserId, ct);
        return NoContent();
    }

    private string CurrentUserId =>
        User.FindFirstValue("sub") ?? throw new UnauthorizedAccessException();

    /// <summary>Người có quyền quản lý được thấy cả gói đang ẩn; người khác chỉ thấy gói đang hoạt động.</summary>
    private async Task<bool> CanManageAsync()
        => (await authorization.AuthorizeAsync(
                User, (object?)null, $"{PermissionPolicy.Prefix}{Permissions.ManageServicePackage}")).Succeeded;
}
