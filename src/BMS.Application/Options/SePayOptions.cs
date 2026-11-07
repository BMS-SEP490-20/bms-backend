namespace BMS.Application.Options;

/// <summary>Live SePay bank-transfer settings. Values come from environment variables, not source control.</summary>
public sealed class SePayOptions
{
    public const string SectionName = "SePay";

    public string AccountNumber { get; set; } = string.Empty;
    public string BankCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string WebhookApiKey { get; set; } = string.Empty;
    public string PaymentPrefix { get; set; } = "SEVQR";
}
