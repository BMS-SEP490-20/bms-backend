using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs.Billing;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for automated medical billing fee calculations (UC53 / Calculate Fees).
/// Conforms to: Production Engineering Standards & AGENTS-DESIGN.md.
/// </summary>
public interface ICalculateFeeService
{
    /// <summary>
    /// Calculates gross and net fees based on test packages, individual tests, service type, BHYT insurance, and vouchers.
    /// </summary>
    Task<CalculateFeeResultDto> CalculateFeeAsync(CalculateFeeRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates estimated fee for an existing laboratory test order.
    /// </summary>
    Task<CalculateFeeResultDto?> EstimateOrderFeeAsync(string orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a standalone voucher code against order parameters.
    /// </summary>
    Task<VoucherValidationResultDto> ValidateVoucherAsync(ValidateVoucherRequestDto request, CancellationToken cancellationToken = default);
}
