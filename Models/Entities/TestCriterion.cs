using System;
using System.Collections.Generic;

namespace Testorder_service.Models.Entities;

public partial class TestCriterion
{
    public string CriteriaId { get; set; } = null!;

    public string CriteriaName { get; set; } = null!;

    public string? Description { get; set; }

    public decimal? ReferenceMin { get; set; }

    public decimal? ReferenceMax { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public bool? ActiveFlag { get; set; }

    public string? Unit { get; set; }

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual ICollection<TestPackageCriterion> TestPackageCriteria { get; set; } = new List<TestPackageCriterion>();

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual ICollection<TestParameter> TestParameters { get; set; } = new List<TestParameter>();
}
