using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Billing;

// Generate Receipt (I3-019) DTOs, moved out of the legacy payment_service.Models namespace.
// PatientId is a string: BMS patient ids are Mongo ids, not Guids as in the old payment microservice.

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
    public string PatientId { get; set; } = string.Empty;

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
    public string PatientId { get; set; } = string.Empty;

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

/// <summary>Payload for recording a payment transaction.</summary>
public class CreatePaymentRequest
{
    /// <summary>Patient id.</summary>
    [Required]
    public string PatientId { get; set; } = string.Empty;

    /// <summary>Paid amount in VND.</summary>
    [Range(0, 1_000_000_000)]
    public decimal Amount { get; set; }

    /// <summary>Gateway transaction code.</summary>
    [MaxLength(200)]
    public string? TransactionCode { get; set; }

    /// <summary>VNPay transaction reference (vnp_TxnRef).</summary>
    [MaxLength(100)]
    public string? TxnRef { get; set; }

    /// <summary>Time the payment was made, if already paid.</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>Optional client timestamp; the server time is used when omitted.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Free-text notes.</summary>
    public string? Notes { get; set; }
}

/// <summary>Payload for changing a payment's status and gateway references.</summary>
public class UpdatePaymentStatusRequest
{
    /// <summary>New status: Pending, Success or Failed.</summary>
    [Required]
    public string Status { get; set; } = string.Empty;

    /// <summary>Gateway transaction code.</summary>
    public string? TransactionCode { get; set; }

    /// <summary>VNPay transaction reference.</summary>
    public string? TxnRef { get; set; }
}
