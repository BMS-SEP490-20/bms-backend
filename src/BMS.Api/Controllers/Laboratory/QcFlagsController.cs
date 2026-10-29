using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing internal quality control (IQC) Westgard rules and active QC flags (UC19 / UC73 / Iteration 3).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Route("api/qc/flags")]
[Route("api/v1/qc/flags")]
// WHY: UC74: Lab; Manager view. (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleSets.LabOrManager)]
public class QcFlagsController : ControllerBase
{
    private readonly IQcService _qcService;

    public QcFlagsController(IQcService qcService)
    {
        _qcService = qcService;
    }

    /// <summary>
    /// Retrieves active IQC violations and Westgard warning alerts.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetQcFlags(CancellationToken ct)
    {
        var flags = await _qcService.GetActiveQcFlagsAsync(ct);
        return Ok(new
        {
            success = true,
            data = flags,
            total = flags.Count,
            message = "Đã tải danh sách cảnh báo vi phạm chất lượng xét nghiệm (IQC Flags) thành công."
        });
    }
}
