using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace BMS.Core.Entities;

/// <summary>
/// Status of an invoice transaction.
/// </summary>
public enum InvoiceStatus
{
    Unpaid = 0,
    Paid = 1,
    PartiallyRefunded = 2,
    Refunded = 3,
    Voided = 4
}

/// <summary>
/// Payment methods supported for medical billing.
/// </summary>
public enum PaymentMethod
{
    Cash = 1,
    VNPay = 2,
    BankTransfer = 3,
    CreditCard = 4,
    MoMo = 5,
    BankingQR = 6,
    Reception = 7
}

/// <summary>
/// Itemized fee line item in an invoice.
/// </summary>
public class InvoiceItem
{
    [BsonElement("itemCode")]
    public string ItemCode { get; set; } = string.Empty;

    [BsonElement("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [BsonElement("quantity")]
    public int Quantity { get; set; } = 1;

    [BsonElement("unitPrice")]
    public decimal UnitPrice { get; set; }

    [BsonElement("totalPrice")]
    public decimal TotalPrice => Quantity * UnitPrice;
}

/// <summary>
/// Represents a medical billing invoice / receipt issued to a patient (UC23).
/// </summary>
public class Invoice
{
    [BsonId]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [BsonElement("invoiceCode")]
    public string InvoiceCode { get; set; } = string.Empty;

    [BsonElement("patientId")]
    public string PatientId { get; set; } = string.Empty;

    [BsonElement("appointmentId")]
    public string? AppointmentId { get; set; }

    [BsonElement("items")]
    public List<InvoiceItem> Items { get; set; } = [];

    [BsonElement("subTotal")]
    public decimal SubTotal { get; set; }

    [BsonElement("discountAmount")]
    public decimal DiscountAmount { get; set; }

    [BsonElement("totalAmount")]
    public decimal TotalAmount { get; set; }

    [BsonElement("status")]
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Unpaid;

    [BsonElement("paymentMethod")]
    public PaymentMethod? PaymentMethod { get; set; }

    [BsonElement("transactionReference")]
    public string? TransactionReference { get; set; }

    [BsonElement("patientName")]
    public string? PatientName { get; set; }

    [BsonElement("patientPhone")]
    public string? PatientPhone { get; set; }

    [BsonElement("cashierId")]
    public string? CashierId { get; set; }

    [BsonElement("cashierName")]
    public string? CashierName { get; set; }

    [BsonElement("receiptNumber")]
    public string? ReceiptNumber { get; set; }

    [BsonElement("printCount")]
    public int PrintCount { get; set; } = 0;

    [BsonElement("lastPrintedAt")]
    public DateTime? LastPrintedAt { get; set; }

    [BsonElement("paidAt")]
    public DateTime? PaidAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
