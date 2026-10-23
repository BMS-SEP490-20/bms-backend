using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs.Reports;

/// <summary>
/// Query filter parameters for revenue reporting (UC78).
/// </summary>
public class RevenueFilterParameters
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string Period { get; set; } = "monthly"; // daily | weekly | monthly | quarterly | yearly
    public string? Category { get; set; }
    public string? PaymentMethod { get; set; }
    public string? ServiceType { get; set; }
}

/// <summary>
/// Aggregated financial summary KPIs for laboratory operations.
/// </summary>
public class RevenueSummaryDto
{
    public decimal GrossRevenue { get; set; }
    public decimal BhytCoverageTotal { get; set; }
    public decimal DiscountsTotal { get; set; }
    public decimal SurchargesTotal { get; set; }
    public decimal NetRevenue { get; set; }
    public int TotalOrdersCount { get; set; }
    public decimal AverageOrderValue { get; set; } // ARPU / AOV
    public double GrowthRateVsPreviousPeriod { get; set; } // Percentage e.g. +14.2%
    public string FormattedGrossRevenue { get; set; } = string.Empty;
    public string FormattedNetRevenue { get; set; } = string.Empty;
}

/// <summary>
/// Time-series trend point for revenue line and bar charts.
/// </summary>
public class RevenueTrendPointDto
{
    public string DateLabel { get; set; } = string.Empty; // e.g. "Tháng 01", "2026-10-15"
    public decimal GrossAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal BhytAmount { get; set; }
    public int OrderCount { get; set; }
}

/// <summary>
/// Revenue breakdown by test category (Sinh hóa, Huyết học, Miễn dịch...).
/// </summary>
public class RevenueByCategoryDto
{
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
    public int OrderCount { get; set; }
}

/// <summary>
/// Revenue breakdown by payment method (VietQR, Tiền mặt, BHYT...).
/// </summary>
public class RevenueByPaymentMethodDto
{
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
    public int TransactionCount { get; set; }
}

/// <summary>
/// Revenue breakdown by service channel (Tại viện vs Lấy mẫu tận nơi).
/// </summary>
public class RevenueByChannelDto
{
    public string Channel { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public double Percentage { get; set; }
    public int OrderCount { get; set; }
}

/// <summary>
/// Top revenue-generating referral doctors or hospital departments.
/// </summary>
public class RevenueByReferralDto
{
    public string Name { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int OrderCount { get; set; }
}

/// <summary>
/// Granular transaction record for export to accounting / Excel.
/// </summary>
public class RevenueExportItemDto
{
    public string TransactionNumber { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal BhytCoverageAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string CashierName { get; set; } = string.Empty;
}

/// <summary>
/// Comprehensive composite report payload containing all revenue dimensions.
/// </summary>
public class ComprehensiveRevenueReportDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string Period { get; set; } = string.Empty;
    public RevenueSummaryDto Summary { get; set; } = new();
    public List<RevenueTrendPointDto> Trend { get; set; } = new();
    public List<RevenueByCategoryDto> ByCategory { get; set; } = new();
    public List<RevenueByPaymentMethodDto> ByPaymentMethod { get; set; } = new();
    public List<RevenueByChannelDto> ByChannel { get; set; } = new();
    public List<RevenueByReferralDto> TopReferrals { get; set; } = new();
}
