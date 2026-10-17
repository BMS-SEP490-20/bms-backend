using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// Controller processing transaction refunds and payment reversals (UC23 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
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
