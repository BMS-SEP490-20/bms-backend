using BMS.Core.Entities;
using System;
using System.Collections.Generic;

namespace BMS.Application.DTOs.Billing;

public class ReceiptPrintItemDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
}

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

public class PayInvoiceRequest
{
    public PaymentMethod PaymentMethod { get; set; } = BMS.Core.Entities.PaymentMethod.Cash;
    public string? TransactionReference { get; set; }
}

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
