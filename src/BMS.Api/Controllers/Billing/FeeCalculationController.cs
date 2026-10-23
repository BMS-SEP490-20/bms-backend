using System;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BMS.Api.Controllers.Billing;

/// <summary>
/// Controller providing automated fee calculation, insurance co-pay resolution, and voucher evaluation (UC53 / Calculate Fees).
/// Conforms to: Production Engineering Standards & AGENTS-DESIGN.md.
/// </summary>
[ApiController]
[Route("api/billing")]
[Route("api/fees")]
public class FeeCalculationController : ControllerBase
{
    private readonly ICalculateFeeService _calculateFeeService;

    public FeeCalculationController(ICalculateFeeService calculateFeeService)
    {
        _calculateFeeService = calculateFeeService;
    }

    /// <summary>
    /// Calculates gross and net fees based on test packages, individual tests, service type, BHYT insurance, and vouchers (UC53).
    /// </summary>
    [HttpPost("calculate-fee")]
    public async Task<IActionResult> CalculateFee([FromBody] CalculateFeeRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return BadRequest(new { success = false, message = "Request body cannot be null." });
        }

        try
        {
            var result = await _calculateFeeService.CalculateFeeAsync(request, cancellationToken);
            return Ok(new
            {
                success = true,
                data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while calculating fees: " + ex.Message
            });
        }
    }

    /// <summary>
    /// Validates a promotional voucher code against order parameters.
    /// </summary>
    [HttpPost("validate-voucher")]
    public async Task<IActionResult> ValidateVoucher([FromBody] ValidateVoucherRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.VoucherCode))
        {
            return BadRequest(new { success = false, message = "Voucher code cannot be empty." });
        }

        try
        {
            var result = await _calculateFeeService.ValidateVoucherAsync(request, cancellationToken);
            return Ok(new
            {
                success = true,
                data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while validating voucher: " + ex.Message
            });
        }
    }

    /// <summary>
    /// Estimates fees for an existing test order by Order ID or Order Number.
    /// </summary>
    [HttpGet("estimate/{orderId}")]
    public async Task<IActionResult> EstimateOrderFee(string orderId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return BadRequest(new { success = false, message = "Order ID cannot be empty." });
        }

        try
        {
            var result = await _calculateFeeService.EstimateOrderFeeAsync(orderId, cancellationToken);
            if (result == null)
            {
                return NotFound(new
                {
                    success = false,
                    message = $"Order '{orderId}' was not found to estimate fees."
                });
            }

            return Ok(new
            {
                success = true,
                data = result
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "An error occurred while estimating order fee: " + ex.Message
            });
        }
    }
}
