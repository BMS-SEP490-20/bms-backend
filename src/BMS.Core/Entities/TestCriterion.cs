namespace BMS.Core.Entities;

/// <summary>Tiêu chí xét nghiệm – map với collection "test_criteria" (_id dạng chuỗi, vd: CRT-RBC).</summary>
public class TestCriterion
{
    public string Id { get; set; } = default!;
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Unit { get; set; } = default!;
    public string Category { get; set; } = default!;
}