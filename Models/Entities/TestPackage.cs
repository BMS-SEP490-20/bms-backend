using System;
using System.Collections.Generic;

namespace Testorder_service.Models.Entities;

public partial class TestPackage
{
    public string PackageId { get; set; } = null!;

    public string PackageName { get; set; } = null!;

    public string? Description { get; set; }

    public decimal? Price { get; set; }

    public string? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual ICollection<TestPackageCriterion> TestPackageCriteria { get; set; } = new List<TestPackageCriterion>();
}
