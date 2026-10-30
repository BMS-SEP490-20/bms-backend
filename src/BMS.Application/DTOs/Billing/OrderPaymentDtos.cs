namespace BMS.Application.DTOs.Billing;

/// <summary>Order payment request (UC53/UC55). No amount fields: the server computes every number.</summary>
public sealed class OrderPaymentRequestDto
{
    /// <summary>15-character BHYT card, optional.</summary>
    public string? InsuranceCardNumber { get; set; }
    /// <summary>BMS10 or FREEHOME, optional.</summary>
    public string? VoucherCode { get; set; }
    /// <summary>Sample collected at home (+150,000, not covered by BHYT).</summary>
    public bool HomeCollection { get; set; }
    /// <summary>Counter: Cash or POS. Online: VNPay.</summary>
    public string PaymentMethod { get; set; } = "Cash";
}

/// <summary>Outcome of an order payment.</summary>
public sealed class OrderPaymentResultDto
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string InvoiceCode { get; set; } = string.Empty;
    public string? ReceiptNumber { get; set; }
    /// <summary>Paid (counter) or Unpaid (VNPay waiting for a verified callback).</summary>
    public string InvoiceStatus { get; set; } = string.Empty;
    public CalculateFeeResultDto Fee { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}
