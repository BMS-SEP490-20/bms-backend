using System;
using System.Collections.Generic;
using BMS.Core.Entities;

namespace BMS.Application.DTOs.Billing;

/// <summary>
/// Itemized line item representation for printing medical test invoices and receipts.
/// </summary>
public class ReceiptPrintItemDto
{
    /// <summary>
    /// Unique code identifying the billable test item or service.
    /// </summary>
    public string ItemCode { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the test item or laboratory service.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Number of units requested for the test item.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Price per single unit of the test item.
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Total price calculated for the line item (Quantity * UnitPrice).
    /// </summary>
    public decimal TotalPrice { get; set; }
}

/// <summary>
/// Comprehensive data transfer object containing all necessary fields for rendering and printing an official billing receipt.
/// </summary>
public class ReceiptPrintDataDto
{
    // Clinic Information
    public string ClinicName { get; set; } = "HỆ THỐNG PHÒNG KHÁM XÉT NGHIỆM BMS";
    public string ClinicAddress { get; set; } = "Khu Công nghệ cao, TP. Thủ Đức, TP. Hồ Chí Minh";
    public string ClinicHotline { get; set; } = "1900 6868 - (028) 3822 5588";
    public string ClinicTaxCode { get; set; } = "0314567890";
    public string ClinicWebsite { get; set; } = "https://bms.med.vn";

    // Receipt Metadata
    public string ReceiptNumber { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string InvoiceCode { get; set; } = string.Empty;
    public DateTime PrintedAt { get; set; } = DateTime.UtcNow;
    public string CashierId { get; set; } = string.Empty;
    public string CashierName { get; set; } = string.Empty;
    public int PrintCount { get; set; } = 1;
    public bool IsReprint => PrintCount > 1;
    public string? ReprintWarning => IsReprint ? $"[BẢN IN LẠI LẦN THỨ {PrintCount}] - KHÔNG CÓ GIÁ TRỊ THANH TOÁN KÉP" : null;

    // Patient Information
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientPhone { get; set; }
    public string? AppointmentId { get; set; }

    // Line items
    public List<ReceiptPrintItemDto> Items { get; set; } = [];

    // Financial calculations
    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string TotalAmountInWords { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = "Tiền mặt (Cash)";
    public string? TransactionReference { get; set; }
    public DateTime? PaidAt { get; set; }

    // Verification
    public string VerificationUrl { get; set; } = string.Empty;
    public string VerificationQrPayload { get; set; } = string.Empty;
}

/// <summary>
/// Request payload required to create a new medical billing invoice.
/// </summary>
public class CreateInvoiceRequest
{
    public string PatientId { get; set; } = string.Empty;
    public string? PatientName { get; set; }
    public string? PatientPhone { get; set; }
    public string? AppointmentId { get; set; }
    public List<InvoiceItem> Items { get; set; } = [];
    public decimal DiscountAmount { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
}

/// <summary>
/// Request payload submitted to process an invoice payment.
/// </summary>
public class PayInvoiceRequest
{
    public PaymentMethod PaymentMethod { get; set; } = BMS.Core.Entities.PaymentMethod.Cash;
    public string? TransactionReference { get; set; }
}

/// <summary>
/// Data transfer object representing the result of a receipt authenticity verification check.
/// </summary>
public class ReceiptVerificationResultDto
{
    public bool IsValid { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public string InvoiceCode { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string TotalAmountInWords { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int PrintCount { get; set; }
}
