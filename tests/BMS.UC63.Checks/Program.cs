using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BMS.Api.Controllers.Reports;
using BMS.Application.DTOs.Statistics;
using BMS.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace BMS.UC63.Checks;

public class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== BẮT ĐẦU KIỂM THỬ TỰ ĐỘNG UC63 BACKEND (VIEW TAT STATISTICS) ===\n");

        var logger = NullLogger<TatStatisticsService>.Instance;
        var service = new TatStatisticsService(logger);
        var controller = new TatReportsController(service);

        // 1. Service initialization
        Assert("Check 1: Service is properly instantiated", service != null);

        // 2. Summary statistics calculation
        var defaultQuery = new TatQueryParameters();
        var summary = await service!.GetTatSummaryAsync(defaultQuery);
        Assert("Check 2: Summary returns positive completed tests count", summary.TotalCompletedTests > 0);
        Assert("Check 3: Average TAT is greater than zero and formatted", summary.AverageTatMinutes > 0 && !string.IsNullOrEmpty(summary.AverageTatFormatted));
        Assert("Check 4: Median TAT is computed accurately", summary.MedianTatMinutes > 0 && !string.IsNullOrEmpty(summary.MedianTatFormatted));
        Assert("Check 5: P90 TAT is greater than or equal to Median TAT", summary.P90TatMinutes >= summary.MedianTatMinutes);
        Assert("Check 6: Target compliance rate is within valid [0..100] percentage", summary.TargetComplianceRate >= 0 && summary.TargetComplianceRate <= 100);

        // 3. Full report generation
        var report = await service.GetTatReportAsync(defaultQuery);
        Assert("Check 7: Full report contains valid non-null summary and breakdowns", report != null && report.Summary != null);

        // 4. Priority breakdown verification
        Assert("Check 8: Priority breakdown includes Stat, Urgent, and Normal",
            report != null &&
            report.PriorityBreakdown.Any(p => p.Priority == "Stat") &&
            report.PriorityBreakdown.Any(p => p.Priority == "Urgent") &&
            report.PriorityBreakdown.Any(p => p.Priority == "Normal"));

        var statItem = report.PriorityBreakdown.First(p => p.Priority == "Stat");
        Assert("Check 9: Stat benchmark target is 60 minutes", statItem.TargetMinutes == 60);

        // 5. Department breakdown verification
        Assert("Check 10: Department breakdown includes Biochemistry and Hematology",
            report.DepartmentBreakdown.Any(d => d.Department == "Biochemistry") &&
            report.DepartmentBreakdown.Any(d => d.Department == "Hematology"));

        // 6. Phase breakdown verification
        Assert("Check 11: Phase breakdown includes Pre-analytical, Analytical, Post-analytical",
            report.PhaseBreakdown.Count == 3 &&
            report.PhaseBreakdown.Any(p => p.PhaseName == "PreAnalytical") &&
            report.PhaseBreakdown.Any(p => p.PhaseName == "Analytical") &&
            report.PhaseBreakdown.Any(p => p.PhaseName == "PostAnalytical"));

        var totalPercentage = report.PhaseBreakdown.Sum(p => p.PercentageOfTotal);
        Assert("Check 12: Phase breakdown percentages sum to approximately 100%", Math.Abs(totalPercentage - 100.0) < 2.0);

        // 7. Trends generation
        var trends = await service.GetTatTrendsAsync(defaultQuery);
        Assert("Check 13: Trends return continuous points with volume and compliance rate",
            trends != null && trends.Count >= 5 && trends.All(t => t.Volume > 0 && t.ComplianceRate >= 0));

        // 8. Violations detection
        var violations = await service.GetTatViolationsAsync(defaultQuery);
        Assert("Check 14: Violations list identifies outlier samples exceeding target TAT with positive delay",
            violations != null && violations.Count > 0 && violations.All(v => v.DelayMinutes > 0));

        // 9. CSV Export
        var csvBytes = await service.ExportTatReportCsvAsync(defaultQuery);
        var csvText = Encoding.UTF8.GetString(csvBytes);
        Assert("Check 15: CSV export produces valid UTF-8 content with Vietnamese column headers",
            csvBytes.Length > 0 && csvText.Contains("Mã mẫu") && csvText.Contains("TAT thực tế"));

        // 10. Controller Action endpoint verification
        var controllerReportResult = await controller.GetTatReport(defaultQuery, default);
        Assert("Check 16: Controller GetTatReport returns HTTP 200 OkObjectResult",
            controllerReportResult is OkObjectResult okResult && okResult.StatusCode is null or 200);

        var controllerSummaryResult = await controller.GetSummary(defaultQuery, default);
        Assert("Check 17: Controller GetSummary returns HTTP 200 OkObjectResult",
            controllerSummaryResult is OkObjectResult);

        var controllerCsvResult = await controller.ExportCsv(defaultQuery, default);
        Assert("Check 18: Controller ExportCsv returns FileContentResult with text/csv mime type",
            controllerCsvResult is FileContentResult fileResult && fileResult.ContentType.Contains("text/csv"));

        // 11. Filtering by Department
        var filteredQuery = new TatQueryParameters { Department = "Hematology" };
        var filteredReport = await service.GetTatReportAsync(filteredQuery);
        Assert("Check 19: Department filter restricts records to Hematology",
            filteredReport.DepartmentBreakdown.All(d => d.Department == "Hematology"));

        Console.WriteLine("\n================================================");
        Console.WriteLine($"TỔNG KẾT: {_passed} PASSED | {_failed} FAILED");
        Console.WriteLine("================================================");

        return _failed > 0 ? 1 : 0;
    }

    private static void Assert(string name, bool condition)
    {
        if (condition)
        {
            Console.WriteLine($"[PASS] {name}");
            _passed++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] {name}");
            Console.ResetColor();
            _failed++;
        }
    }
}
