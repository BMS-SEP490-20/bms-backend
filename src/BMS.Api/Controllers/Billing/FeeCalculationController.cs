using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// UC53 fee calculation (BHYT by the card's 3rd character, 379,500 threshold, vouchers, 1,000 rounding).
/// Thin controller: errors (400 MSG-ERR-FIN-01 invalid card, 400 unknown item) are AppExceptions mapped by the
/// global handler; exception text is never returned.
/// </summary>
[ApiController]
[Route("api/billing")]
[Route("api/fees")]
// WHY: UC53: fee calculation only by Receptionist; Manager denied (decision 10). (ROLE-PERMISSION-MATRIX §1, TIEU-CHI §1.2)
[Authorize(Roles = RoleNames.Receptionist)]
public class FeeCalculationController(ICalculateFeeService calculateFeeService) : ControllerBase
{
    /// <summary>Fee preview from catalog codes; client prices are not accepted.</summary>
    [HttpPost("calculate-fee")]
    [ProducesResponseType(typeof(CalculateFeeResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CalculateFee([FromBody] CalculateFeeRequestDto request, CancellationToken cancellationToken) =>
        Ok(new { success = true, data = await calculateFeeService.CalculateFeeAsync(request, cancellationToken) });

    /// <summary>Checks a voucher (BMS10 capped at 200,000; FREEHOME).</summary>
    [HttpPost("validate-voucher")]
    public async Task<IActionResult> ValidateVoucher([FromBody] ValidateVoucherRequestDto request, CancellationToken cancellationToken) =>
        Ok(new { success = true, data = await calculateFeeService.ValidateVoucherAsync(request, cancellationToken) });

    /// <summary>Fee of a stored order (same numbers as payment).</summary>
    [HttpGet("estimate/{orderId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EstimateOrderFee(string orderId, [FromQuery] string? insuranceCard, [FromQuery] string? voucher,
        [FromQuery] bool homeCollection, CancellationToken cancellationToken)
    {
        var result = await calculateFeeService.EstimateOrderFeeAsync(orderId, insuranceCard, voucher, homeCollection, cancellationToken);
        return result == null
            ? NotFound(new { success = false, code = "ORDER_NOT_FOUND", message = "Không tìm thấy đơn xét nghiệm." })
            : Ok(new { success = true, data = result });
    }
}
