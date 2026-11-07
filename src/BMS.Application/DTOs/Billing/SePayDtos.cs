using System.Text.Json.Serialization;

namespace BMS.Application.DTOs.Billing;

public sealed class SePayWebhookPayload
{
    public long Id { get; set; }
    public string? Gateway { get; set; }
    public string? AccountNumber { get; set; }
    public string? Code { get; set; }
    public string? Content { get; set; }
    public string? TransferType { get; set; }
    public decimal TransferAmount { get; set; }
    public string? ReferenceCode { get; set; }
}

public sealed class SePayCheckoutDto
{
    public string AppointmentId { get; set; } = string.Empty;
    public string AppointmentCode { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public string VisitDate { get; set; } = string.Empty;
    public string SlotTime { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string? TransactionId { get; set; }
    public string? PaidAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Paid { get; set; }
    public decimal Amount { get; set; }
    public string TransferContent { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string QrUrl { get; set; } = string.Empty;
}
