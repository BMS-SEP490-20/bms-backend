using Microsoft.AspNetCore.Authorization;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// Controller processing transaction refunds and payment reversals (UC23 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
// WHY: UC55 refund: Manager (C5 default). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleNames.Manager)]
public class RefundsController : ControllerBase
{
    /// <summary>
    /// Processes a payment refund request for cancelled appointments or duplicate payments.
    /// </summary>
    [HttpPost]
    public IActionResult ProcessRefund([FromBody] object payload)
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Refund processing API endpoint is scheduled for Iteration 2."
        });
    }
}
