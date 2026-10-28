using System;
using System.Collections.Generic;

namespace Testorder_service.Models.Entities;

public partial class BloodSample
{
    public string SampleId { get; set; } = null!;

    public string AppointmentId { get; set; } = null!;

    public string SampleType { get; set; } = null!;

    public DateTime? CollectedAt { get; set; }

    public string? Status { get; set; }

    public string? StorageLocation { get; set; }

    public string? Notes { get; set; }

    public decimal? Volume { get; set; }

    public string? LabUserId { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual Appointment Appointment { get; set; } = null!;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual LabUserProfile? LabUser { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual ICollection<TestResult> TestResults { get; set; } = new List<TestResult>();
}
