using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Laboratory;

/// <summary>
/// Controller managing laboratory analyzer run batches and sample groupings (UC18 / Iteration 2).
/// Conforms to: Production Standards &amp; AGENTS-CODE.md.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class TestBatchesController : ControllerBase
{
    /// <summary>
    /// Retrieves active laboratory testing batches.
    /// </summary>
    [HttpGet]
    public IActionResult GetBatches()
    {
        return Ok(new
        {
            success = true,
            status = "Scheduled",
            milestone = "Iteration 2",
            message = "Lab testing batch management endpoint is scheduled for Iteration 2."
        });
    }
}
