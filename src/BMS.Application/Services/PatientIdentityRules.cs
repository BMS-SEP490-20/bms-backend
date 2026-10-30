using System.Text.RegularExpressions;
using BMS.Core.Exceptions;

namespace BMS.Application.Services;

/// <summary>
/// Identity rules for reception (BA:L25, L32; TIEU-CHI Bước 1 "Mã BN"; SMK-LT-01).
/// </summary>
public static partial class PatientIdentityRules
{
    /// <summary>CCCD: exactly 12 digits.</summary>
    [GeneratedRegex(@"^\d{12}$")]
    public static partial Regex Cccd();

    /// <summary>Vietnamese mobile: 10 digits starting 03, 05, 07, 08 or 09.</summary>
    [GeneratedRegex(@"^0[35789]\d{8}$")]
    public static partial Regex Phone();

    /// <summary>Throws 400 when CCCD (if given) or phone break the format rules.</summary>
    public static void Validate(string? cccd, string phone)
    {
        if (!string.IsNullOrWhiteSpace(cccd) && !Cccd().IsMatch(cccd.Trim()))
            throw new DomainValidationException("MSG-ERR-REC-03", "Số CCCD phải gồm đúng 12 chữ số.");
        if (!Phone().IsMatch(phone.Trim()))
            throw new DomainValidationException("MSG-ERR-REC-04", "Số điện thoại phải gồm 10 chữ số, bắt đầu bằng 03, 05, 07, 08 hoặc 09.");
    }

    /// <summary>Formats the patient code PAT-YYMMDD-XXXX (never derived from CCCD).</summary>
    public static string FormatPatientCode(DateOnly localDate, long sequence) => $"PAT-{localDate:yyMMdd}-{sequence:D4}";
}
