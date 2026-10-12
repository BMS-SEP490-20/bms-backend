using System;
using System.Collections.Generic;

namespace Testorder_service.Models.Entities;

public partial class TestPackageCriterion
{
    public string TpcId { get; set; } = null!;

    public string TestPackageId { get; set; } = null!;

    public string TestCriteriaId { get; set; } = null!;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual TestCriterion TestCriteria { get; set; } = null!;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public virtual TestPackage TestPackage { get; set; } = null!;
}
