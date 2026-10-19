using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.Result;

public class EnterResultRequest
{
    [Required]
    public string SampleId { get; set; } = string.Empty;

    public string? PackageId { get; set; }
    public string? EquipmentCode { get; set; }

    [Required, MinLength(1)]
    public List<ParameterInput> Parameters { get; set; } = new();

    public string? Notes { get; set; }
    public bool ConfirmRerunChecked { get; set; }
}

public class ParameterInput
{
    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public double Value { get; set; }

    public string Unit { get; set; } = string.Empty;
    public double? RefLow { get; set; }
    public double? RefHigh { get; set; }
}

public class ParameterResultDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Flag { get; set; } = "Normal";
    public bool IsPanic { get; set; }
}

public class EnterResultResponse
{
    public string ResultId { get; set; } = string.Empty;
    public string ResultCode { get; set; } = string.Empty;
    public string SampleId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsPanic { get; set; }
    public List<ParameterResultDto> Parameters { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public class ApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
