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

namespace payment_service.Models
{
    /// <summary>
    /// Itemized breakdown line for a test package or laboratory service in a receipt.
    /// </summary>
    public class ReceiptItemDto
    {
        /// <summary>
        /// Identifier of the requested test package or service.
        /// </summary>
        public string PackageId { get; set; } = string.Empty;

        /// <summary>
        /// Display name of the test package.
        /// </summary>
        public string PackageName { get; set; } = string.Empty;

        /// <summary>
        /// Quantity of service units requested.
        /// </summary>
        public int Quantity { get; set; } = 1;

        /// <summary>
        /// Unit price of the package before discounts and taxes.
        /// </summary>
        public decimal UnitPrice { get; set; }

        /// <summary>
        /// Calculated subtotal for this item (Quantity * UnitPrice).
        /// </summary>
        public decimal Amount { get; set; }
    }

    /// <summary>
    /// Electronic receipt or payment invoice for requested test packages.
    /// Contains itemized fees, taxes, discounts, and receptionist audit trail.
    /// </summary>
    public class ReceiptDto
    {
        /// <summary>
        /// Unique electronic receipt reference code (e.g., REC-20261021-A1B2C3).
        /// </summary>
        public string ReceiptNumber { get; set; } = string.Empty;

        /// <summary>
        /// Associated appointment identifier.
        /// </summary>
        public string? AppointmentId { get; set; }

        /// <summary>
        /// Unique patient identifier.
        /// </summary>
        public Guid PatientId { get; set; }

        /// <summary>
        /// Full name of the patient receiving the laboratory services.
        /// </summary>
        public string? PatientName { get; set; }

        /// <summary>
        /// Contact phone number of the patient.
        /// </summary>
        public string? PatientPhone { get; set; }

        /// <summary>
        /// Identifier of the receptionist cashier generating the receipt.
        /// </summary>
        public string? ReceptionistId { get; set; }

        /// <summary>
        /// Name of the receptionist cashier who processed the receipt.
        /// </summary>
        public string? ReceptionistName { get; set; }

        /// <summary>
        /// Method of settlement (e.g., Cash, VNPay, BankTransfer, CreditCard).
        /// </summary>
        public string PaymentMethod { get; set; } = "Cash";

        /// <summary>
        /// Settlement status (e.g., Paid, Pending, Refunded).
        /// </summary>
        public string Status { get; set; } = "Paid";

        /// <summary>
        /// Timestamp of receipt issuance.
        /// </summary>
        public DateTime ReceiptDate { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// List of itemized test packages or services included in this settlement.
        /// </summary>
        public List<ReceiptItemDto> Items { get; set; } = new List<ReceiptItemDto>();

        /// <summary>
        /// Sum of item amounts prior to tax and discounts.
        /// </summary>
        public decimal SubTotal { get; set; }

        /// <summary>
        /// Value-added tax percentage applied (e.g., 8% or 10%).
        /// </summary>
        public decimal TaxPercent { get; set; }

        /// <summary>
        /// Calculated tax surcharge amount.
        /// </summary>
        public decimal TaxAmount { get; set; }

        /// <summary>
        /// Discount deduction amount.
        /// </summary>
        public decimal DiscountAmount { get; set; }

        /// <summary>
        /// Net payable amount after tax and discounts.
        /// </summary>
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// Optional cashier notes or payment remarks.
        /// </summary>
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Line item input for receipt generation request.
    /// </summary>
    public class ReceiptItemRequest
    {
        /// <summary>
        /// Package or service identifier.
        /// </summary>
        public string PackageId { get; set; } = string.Empty;

        /// <summary>
        /// Package or service name.
        /// </summary>
        public string PackageName { get; set; } = string.Empty;

        /// <summary>
        /// Number of units. Defaults to 1.
        /// </summary>
        public int Quantity { get; set; } = 1;

        /// <summary>
        /// Unit price in local currency.
        /// </summary>
        public decimal UnitPrice { get; set; }
    }

    /// <summary>
    /// Payload submitted by Receptionist to calculate fees and generate an electronic payment receipt.
    /// </summary>
    public class GenerateReceiptRequest
    {
        /// <summary>
        /// Target appointment identifier.
        /// </summary>
        public string? AppointmentId { get; set; }

        /// <summary>
        /// Target patient identifier.
        /// </summary>
        public Guid PatientId { get; set; }

        /// <summary>
        /// Patient full name.
        /// </summary>
        public string? PatientName { get; set; }

        /// <summary>
        /// Patient contact telephone.
        /// </summary>
        public string? PatientPhone { get; set; }

        /// <summary>
        /// Receptionist cashier user identifier.
        /// </summary>
        public string? ReceptionistId { get; set; }

        /// <summary>
        /// Receptionist cashier staff name.
        /// </summary>
        public string? ReceptionistName { get; set; }

        /// <summary>
        /// Chosen payment settlement channel (Cash, VNPay, BankTransfer, CreditCard).
        /// </summary>
        public string? PaymentMethod { get; set; }

        /// <summary>
        /// Value-added tax rate percentage (e.g., 8 or 10).
        /// </summary>
        public decimal? TaxPercent { get; set; }

        /// <summary>
        /// Promotional discount amount.
        /// </summary>
        public decimal? DiscountAmount { get; set; }

        /// <summary>
        /// List of requested test packages to itemize on the receipt.
        /// </summary>
        public List<ReceiptItemRequest> Items { get; set; } = new List<ReceiptItemRequest>();

        /// <summary>
        /// Cashier remarks or payment details.
        /// </summary>
        public string? Notes { get; set; }
    }
}