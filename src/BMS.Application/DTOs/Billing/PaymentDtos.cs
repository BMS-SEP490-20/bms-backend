using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BMS.Core.Entities;

namespace BMS.Application.DTOs.Billing;

/// <summary>
/// Request DTO for processing patient fee payment (UC55 / Process Payment).
/// </summary>
public class ProcessPaymentRequestDto
{
    [Required]
    public string InvoiceId { get; set; } = string.Empty;

    public string? PatientId { get; set; }

    [Required]
    public string PaymentMethod { get; set; } = "VNPay"; // VNPay, MoMo, BankingQR, Reception, Cash

    public string? DiscountCode { get; set; }

    public string? ReturnUrl { get; set; }
}

/// <summary>
/// Request DTO for applying promotional discount codes to an invoice (UC55).
/// </summary>
public class ApplyDiscountRequestDto
{
    [Required]
    public string InvoiceId { get; set; } = string.Empty;

    [Required]
    public string DiscountCode { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO after validating and applying a promotional discount code.
/// </summary>
public class DiscountResultDto
{
    public bool IsValid { get; set; }
    public string DiscountCode { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal OriginalSubTotal { get; set; }
    public decimal NewTotal { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO containing comprehensive invoice checkout information (UC55).
/// </summary>
public class PaymentCheckoutDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public string InvoiceCode { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? PatientPhone { get; set; }
    public string? AppointmentId { get; set; }
    public List<InvoiceItem> Items { get; set; } = new();
    public decimal SubTotal { get; set; }
    public decimal ServiceFee { get; set; } = 0;
    public decimal DiscountAmount { get; set; }
    public string? DiscountCode { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Unpaid";
    public string? PaymentMethod { get; set; }
    public string? TransactionReference { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? PaymentUrl { get; set; }
    public string? QrCodePayload { get; set; }
    public string Message { get; set; } = string.Empty;
}
