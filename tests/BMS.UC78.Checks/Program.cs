using System;
using System.Linq;
using System.Threading.Tasks;
using BMS.Api.Controllers.Reports;
using BMS.Application.DTOs.Reports;
using BMS.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace BMS.UC78.Checks;

public class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== BẮT ĐẦU KIỂM THỬ TỰ ĐỘNG UC78 BACKEND (REVENUE REPORTING) ===\n");

        var logger = NullLogger<RevenueReportService>.Instance;
        var service = new RevenueReportService(logger);
        var controller = new RevenueReportsController(service);

        // 1. Service instantiation
        Assert("Check 1: RevenueReportService is properly instantiated", service != null);

        var filter = new RevenueFilterParameters
        {
            FromDate = DateTime.UtcNow.AddDays(-30),
            ToDate = DateTime.UtcNow,
            Period = "daily"
        };

        // 2. Summary Calculation
        var summary = await service!.GetSummaryAsync(filter);
        Assert("Check 2: GetSummaryAsync returns non-null summary KPIs", summary != null);
        Assert("Check 3: Gross revenue is positive (> 0)", summary!.GrossRevenue > 0);
        Assert("Check 4: Net revenue is positive (> 0) and <= Gross revenue", summary.NetRevenue > 0 && summary.NetRevenue <= summary.GrossRevenue);
        Assert("Check 5: Total orders count is positive (> 0)", summary.TotalOrdersCount > 0);
        Assert("Check 6: Average Order Value (AOV) is positive (> 0)", summary.AverageOrderValue > 0);
        Assert("Check 7: Formatted Gross Revenue includes Vietnamese currency symbol (₫)", summary.FormattedGrossRevenue.Contains("₫"));

        // 3. Trend Time-Series
        var trend = await service.GetTrendAsync(filter);
        Assert("Check 8: GetTrendAsync returns daily trend points for 30-day window", trend != null && trend.Count >= 28);
        var firstTrend = trend?.FirstOrDefault(t => t.OrderCount > 0);
        Assert("Check 9: Trend points contain valid DateLabel, GrossAmount, and NetAmount",
            firstTrend != null && !string.IsNullOrEmpty(firstTrend.DateLabel) && firstTrend.GrossAmount > 0);

        // 4. Revenue By Category
        var categories = await service.GetByCategoryAsync(filter);
        Assert("Check 10: GetByCategoryAsync returns test categories (Biochemistry, Hematology...)",
            categories != null && categories.Count >= 4);
        double totalCatPct = categories?.Sum(c => c.Percentage) ?? 0;
        Assert("Check 11: Category percentage distribution sums to approximately 100%",
            totalCatPct >= 99.0 && totalCatPct <= 101.0);

        // 5. Revenue By Payment Method
        var paymentMethods = await service.GetByPaymentMethodAsync(filter);
        Assert("Check 12: GetByPaymentMethodAsync returns payment methods (VietQR, Tiền mặt, BHYT)",
            paymentMethods != null && paymentMethods.Count >= 3);
        double totalMethodPct = paymentMethods?.Sum(p => p.Percentage) ?? 0;
        Assert("Check 13: Payment method distribution sums to approximately 100%",
            totalMethodPct >= 99.0 && totalMethodPct <= 101.0);

        // 6. Revenue By Channel (InClinic vs HomeCollection)
        var channels = await service.GetByChannelAsync(filter);
        Assert("Check 14: GetByChannelAsync returns channels (Tại viện vs Tận nơi)",
            channels != null && channels.Count >= 2);

        // 7. Export Transactions
        var exportTxns = await service.GetExportTransactionsAsync(filter);
        Assert("Check 15: GetExportTransactionsAsync returns transaction rows for accounting",
            exportTxns != null && exportTxns.Count > 0);
        var firstTxn = exportTxns?[0];
        Assert("Check 16: Transaction row contains TransactionNumber, OrderNumber, and PatientName",
            firstTxn != null && !string.IsNullOrEmpty(firstTxn.TransactionNumber) && !string.IsNullOrEmpty(firstTxn.PatientName));

        // 8. Comprehensive Composite Report
        var compReport = await service.GetComprehensiveReportAsync(filter);
        Assert("Check 17: GetComprehensiveReportAsync returns combined report with summary, trend, and referrals",
            compReport != null && compReport.Summary != null && compReport.Trend?.Count > 0 && compReport.TopReferrals?.Count > 0);

        // 9. Controller Endpoints
        var ctrlReportResult = await controller.GetRevenueReport(filter, default);
        Assert("Check 18: RevenueReportsController GetRevenueReport returns HTTP 200 OkObjectResult",
            ctrlReportResult is OkObjectResult);

        var ctrlSummaryResult = await controller.GetSummary(filter, default);
        Assert("Check 19: RevenueReportsController GetSummary returns HTTP 200 OkObjectResult",
            ctrlSummaryResult is OkObjectResult);

        var ctrlExportResult = await controller.GetExport(filter, default);
        Assert("Check 20: RevenueReportsController GetExport returns HTTP 200 OkObjectResult",
            ctrlExportResult is OkObjectResult);

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
