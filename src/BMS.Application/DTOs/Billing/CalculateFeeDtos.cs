using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Billing;

/// <summary>
/// Request DTO for calculating fees with BHYT insurance co-pay, home collection surcharges, and vouchers (UC53 / Calculate Fees).
/// Conforms to: Production Engineering Standards & AGENTS-DESIGN.md.
/// </summary>
public class CalculateFeeRequestDto
{
    /// <summary>
    /// List of TestPackage IDs or Package Codes to calculate.
    /// </summary>
    public List<string> PackageIds { get; set; } = new();

    /// <summary>
    /// Optional list of individual test criterion IDs or Codes.
    /// </summary>
    public List<string> IndividualTestIds { get; set; } = new();

    /// <summary>
    /// "Clinic" (Tại phòng khám) or "HomeCollection" (Lấy mẫu tận nơi).
    /// Defaults to "Clinic".
    /// </summary>
    public string ServiceType { get; set; } = "Clinic";

    /// <summary>
    /// Vietnam Social Health Insurance (BHYT) 15-character code (e.g., DN4791234567890, TE1012345678901).
    /// </summary>
    public string? InsuranceCardNumber { get; set; }

    /// <summary>
    /// Promotional voucher code (e.g. BMS10, BMS20, FREEHOME, VIP50).
    /// </summary>
    public string? VoucherCode { get; set; }

    /// <summary>
    /// Priority patient flag (Elderly >= 80, Children < 6, Pregnant women, Severe disability).
    /// </summary>
    public bool IsPriorityPatient { get; set; }

    /// <summary>
    /// Patient identity if known.
    /// </summary>
    public string? PatientId { get; set; }
}

/// <summary>
/// Itemized fee line item in calculation breakdown.
/// </summary>
public class FeeItemBreakdownDto
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ItemType { get; set; } = "Package"; // Package, IndividualTest, Surcharge
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal TotalPrice => UnitPrice * Quantity;
    public bool IsBhytEligible { get; set; }
    public decimal BhytCoveredAmount { get; set; }
    public decimal PatientPayAmount { get; set; }
}

/// <summary>
/// Comprehensive calculation result DTO (UC53).
/// </summary>
public class CalculateFeeResultDto
{
    public string CalculationReference { get; set; } = string.Empty;
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;

    public List<FeeItemBreakdownDto> Items { get; set; } = new();

    /// <summary>Sum of laboratory test packages and individual tests.</summary>
    public decimal TestSubTotal { get; set; }

    /// <summary>Service Type: Clinic or HomeCollection.</summary>
    public string ServiceType { get; set; } = "Clinic";

    /// <summary>Surcharge for home phlebotomy (150,000 VND for HomeCollection, 0 for Clinic).</summary>
    public decimal HomeCollectionSurcharge { get; set; }

    /// <summary>Gross Total = TestSubTotal + HomeCollectionSurcharge.</summary>
    public decimal GrossTotal { get; set; }

    /// <summary>Whether valid BHYT was verified.</summary>
    public bool HasInsurance { get; set; }
    public string? InsuranceCardNumber { get; set; }
    public string? InsuranceTierDescription { get; set; }
    public int InsuranceCoveragePercent { get; set; }
    public decimal InsuranceDeduction { get; set; }

    /// <summary>Voucher discounts.</summary>
    public string? VoucherCode { get; set; }
    public bool IsVoucherValid { get; set; }
    public string? VoucherDescription { get; set; }
    public decimal VoucherDiscount { get; set; }

    /// <summary>Priority patient courtesy discount.</summary>
    public bool IsPriorityPatient { get; set; }
    public decimal PriorityDiscount { get; set; }

    /// <summary>Total discounts = VoucherDiscount + PriorityDiscount.</summary>
    public decimal TotalDiscount { get; set; }

    /// <summary>Net final amount payable by patient after all deductions, non-negative.</summary>
    public decimal NetTotalPayable { get; set; }

    /// <summary>Vietnamese reading of NetTotalPayable according to Decree 123/2020/ND-CP.</summary>
    public string TotalInWords { get; set; } = string.Empty;

    public string Currency { get; set; } = "VND";
}

/// <summary>
/// Request DTO for validating a standalone voucher code.
/// </summary>
public class ValidateVoucherRequestDto
{
    [Required]
    public string VoucherCode { get; set; } = string.Empty;

    public decimal CurrentSubTotal { get; set; }
    public string ServiceType { get; set; } = "Clinic";
}

/// <summary>
/// Response DTO for voucher validation.
/// </summary>
public class VoucherValidationResultDto
{
    public bool IsValid { get; set; }
    public string VoucherCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal DiscountAmount { get; set; }
    public decimal EstimatedNetTotal { get; set; }
    public string Message { get; set; } = string.Empty;
}
