namespace BMS.Application.DTOs.Specimens;

/// <summary>Collect specimens for an order after the 2-factor patient check (BA:L41).</summary>
public sealed class CollectSpecimenRequestDto
{
    /// <summary>Order id or ORD- number.</summary>
    public string OrderId { get; set; } = string.Empty;
    /// <summary>Factor 1: full name as told by the patient.</summary>
    public string FullName { get; set; } = string.Empty;
    /// <summary>Factor 2 (either): date of birth…</summary>
    public DateOnly? DateOfBirth { get; set; }
    /// <summary>…or CCCD.</summary>
    public string? IdentityNumber { get; set; }
}

/// <summary>One labelled tube.</summary>
public sealed class CollectedTubeDto
{
    public string Barcode { get; set; } = string.Empty;
    public string TubeColor { get; set; } = string.Empty;
    public string TubeType { get; set; } = string.Empty;
    public int DrawOrder { get; set; }
    public string Inversions { get; set; } = string.Empty;
    public List<string> Tests { get; set; } = new();
}

/// <summary>Collection result with label data (name in upper case, birth year, gender, order number).</summary>
public sealed class CollectSpecimenResultDto
{
    public string OrderId { get; set; } = string.Empty;
    public string OrderNumber { get; set; } = string.Empty;
    public string OrderStatus { get; set; } = string.Empty;
    public string LabelPatientName { get; set; } = string.Empty;
    public int? BirthYear { get; set; }
    public string Gender { get; set; } = string.Empty;
    public DateTime CollectedAtUtc { get; set; }
    public string CollectedBy { get; set; } = string.Empty;
    public List<CollectedTubeDto> Tubes { get; set; } = new();
}
