using System;
using System.Linq;
using System.Threading.Tasks;
using BMS.Api.Controllers.Laboratory;
using BMS.Application.DTOs.QualityControl;
using BMS.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace BMS.UC73.Checks;

public class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== BẮT ĐẦU KIỂM THỬ TỰ ĐỘNG UC73 BACKEND (QC SYSTEM & WESTGARD RULES) ===\n");

        var logger = NullLogger<QcService>.Instance;
        var service = new QcService(logger);
        var qcController = new QualityControlController(service);
        var flagsController = new QcFlagsController(service);

        // 1. Service initialization
        Assert("Check 1: QcService is properly instantiated", service != null);

        // 2. QC Lots retrieval
        var lots = await service!.GetLotsAsync();
        Assert("Check 2: GetLotsAsync returns seeded QC lots", lots != null && lots.Count >= 3);
        var gluLot = lots?.FirstOrDefault(l => l.AnalyteName == "Glucose");
        Assert("Check 3: Glucose lot has valid target mean (5.5) and SD (0.25)",
            gluLot != null && gluLot.TargetMean == 5.5 && gluLot.TargetStandardDeviation == 0.25);

        // 3. Create new QC Lot
        var createDto = new CreateQcLotDto
        {
            LotNumber = "LOT-CHOL-2026A",
            ControlName = "Lipid Control Level 1",
            AnalyteName = "Cholesterol",
            InstrumentId = "ROCHE-COBAS-C501",
            TargetMean = 4.8,
            TargetStandardDeviation = 0.2,
            Unit = "mmol/L"
        };
        var createdLot = await service.CreateLotAsync(createDto);
        Assert("Check 4: CreateLotAsync creates new QC lot with target parameters",
            createdLot != null && createdLot.LotNumber == "LOT-CHOL-2026A" && createdLot.TargetMean == 4.8);

        // 4. Update and Toggle Status
        var updatedLot = await service.UpdateLotAsync(createdLot!.Id, new UpdateQcLotDto { ControlName = "Updated Lipid Control" });
        Assert("Check 5: UpdateLotAsync updates control name", updatedLot.ControlName == "Updated Lipid Control");

        var isToggled = await service.ToggleLotStatusAsync(createdLot.Id);
        Assert("Check 6: ToggleLotStatusAsync deactivates lot (IsActive = false)", isToggled == false);

        // 5. Test QC Run Evaluation: Normal / PASS (|Z| <= 2)
        // Glucose target: 5.5, SD: 0.25 -> value = 5.52 -> Z = (5.52 - 5.5)/0.25 = 0.08
        var passRun = await service.RecordRunAsync(new RecordQcRunDto
        {
            LotId = gluLot!.Id,
            MeasuredValue = 5.52,
            OperatorName = "KTV. Nguyễn Tấn Phát"
        });
        Assert("Check 7: Value within 2s evaluates to PASS (IsLocked = false)",
            passRun.Status == "PASS" && !passRun.IsLocked && Math.Abs(passRun.ZScore) <= 2.0);

        // 6. Test Westgard Rule 1_2s (Warning: |Z| > 2)
        // Value = 6.05 -> Z = (6.05 - 5.5)/0.25 = +2.20s
        // Let's create an isolated test lot so previous history doesn't trigger compound rules
        var testLot1_2s = await service.CreateLotAsync(new CreateQcLotDto
        {
            LotNumber = "LOT-TEST-12S",
            ControlName = "Test Lot 1_2s",
            AnalyteName = "Albumin",
            TargetMean = 40.0,
            TargetStandardDeviation = 2.0,
            Unit = "g/L"
        });
        var warnRun = await service.RecordRunAsync(new RecordQcRunDto
        {
            LotId = testLot1_2s.Id,
            MeasuredValue = 44.4, // Z = (44.4 - 40)/2 = +2.2s
            OperatorName = "KTV. Nguyễn Tấn Phát"
        });
        Assert("Check 8: Excursion between 2s and 3s triggers WARNING_1_2S (IsLocked = false)",
            warnRun.Status == "WARNING_1_2S" && !warnRun.IsLocked && warnRun.ViolatedRules.Contains("1_2s"));

        // 7. Test Westgard Rule 1_3s (Rejection: |Z| > 3)
        var testLot1_3s = await service.CreateLotAsync(new CreateQcLotDto
        {
            LotNumber = "LOT-TEST-13S",
            ControlName = "Test Lot 1_3s",
            AnalyteName = "Potassium",
            TargetMean = 4.0,
            TargetStandardDeviation = 0.2,
            Unit = "mmol/L"
        });
        var reject1_3s = await service.RecordRunAsync(new RecordQcRunDto
        {
            LotId = testLot1_3s.Id,
            MeasuredValue = 4.7, // Z = (4.7 - 4.0)/0.2 = +3.5s > 3s
            OperatorName = "KTV. Nguyễn Tấn Phát"
        });
        Assert("Check 9: Excursion > 3s triggers REJECT_1_3S and locks analyzer (IsLocked = true)",
            reject1_3s.Status == "REJECT_1_3S" && reject1_3s.IsLocked && reject1_3s.ViolatedRules.Contains("1_3s"));

        // 8. Test Westgard Rule 2_2s (Rejection: 2 consecutive runs > 2s on same side)
        var testLot2_2s = await service.CreateLotAsync(new CreateQcLotDto
        {
            LotNumber = "LOT-TEST-22S",
            ControlName = "Test Lot 2_2s",
            AnalyteName = "Sodium",
            TargetMean = 140.0,
            TargetStandardDeviation = 2.0,
            Unit = "mmol/L"
        });
        // Run 1: +2.1s (Warning)
        await service.RecordRunAsync(new RecordQcRunDto
        {
            LotId = testLot2_2s.Id,
            MeasuredValue = 144.2, // Z = +2.1s
            OperatorName = "KTV. Nguyễn Tấn Phát"
        });
        // Run 2: +2.2s (Violates 2_2s!)
        var reject2_2s = await service.RecordRunAsync(new RecordQcRunDto
        {
            LotId = testLot2_2s.Id,
            MeasuredValue = 144.4, // Z = +2.2s
            OperatorName = "KTV. Nguyễn Tấn Phát"
        });
        Assert("Check 10: Two consecutive runs > 2s trigger REJECT_2_2S (Systematic error)",
            reject2_2s.Status == "REJECT_2_2S" && reject2_2s.IsLocked && reject2_2s.ViolatedRules.Contains("2_2s"));

        // 9. Test Westgard Rule R_4s (Rejection: Range of consecutive runs >= 4s)
        var testLotR_4s = await service.CreateLotAsync(new CreateQcLotDto
        {
            LotNumber = "LOT-TEST-R4S",
            ControlName = "Test Lot R_4s",
            AnalyteName = "Calcium",
            TargetMean = 2.4,
            TargetStandardDeviation = 0.1,
            Unit = "mmol/L"
        });
        // Run 1: +2.1s
        await service.RecordRunAsync(new RecordQcRunDto
        {
            LotId = testLotR_4s.Id,
            MeasuredValue = 2.61, // Z = +2.1s
            OperatorName = "KTV. Nguyễn Tấn Phát"
        });
        // Run 2: -2.1s (Range = 2.1 - (-2.1) = 4.2s >= 4s)
        var rejectR_4s = await service.RecordRunAsync(new RecordQcRunDto
        {
            LotId = testLotR_4s.Id,
            MeasuredValue = 2.19, // Z = -2.1s
            OperatorName = "KTV. Nguyễn Tấn Phát"
        });
        Assert("Check 11: Consecutive difference >= 4s triggers REJECT_R_4S (Random error)",
            rejectR_4s.Status == "REJECT_R_4S" && rejectR_4s.IsLocked && rejectR_4s.ViolatedRules.Contains("R_4s"));

        // 10. Levey-Jennings Chart Data
        var ljData = await service.GetLeveyJenningsDataAsync(gluLot.Id);
        Assert("Check 12: Levey-Jennings chart calculates Mean, SD, and +/- 1,2,3 SD boundaries",
            ljData != null && ljData.Mean == 5.5 && ljData.SD == 0.25 && ljData.Plus3SD == 6.25 && ljData.Minus3SD == 4.75);
        Assert("Check 13: Levey-Jennings chart contains historical runs with Z-scores",
            ljData?.Points != null && ljData.Points.Count >= 10);

        // 11. Corrective Action Signoff
        var signoffResult = await service.SignoffCorrectiveActionAsync(reject1_3s.RunId, new QcSignoffRequestDto
        {
            OperatorName = "KTV Trưởng Lab",
            CorrectiveAction = "Đã recalibrate lại kênh điện cực ISE Kali và thay mới điện cực tham chiếu."
        });
        Assert("Check 14: Signoff unlocks run (IsLocked = false) and records corrective action",
            signoffResult != null && !signoffResult.IsLocked);

        // 12. Dashboard Summary
        var summary = await service.GetDashboardSummaryAsync();
        Assert("Check 15: Dashboard summary reports InControlRate >= 0 and ActiveLotsCount > 0",
            summary != null && summary.ActiveLotsCount > 0 && summary.InControlRate >= 0);

        // 13. Active QC Flags
        var flags = await service.GetActiveQcFlagsAsync();
        Assert("Check 16: Active QC flags retrieve warning and rejection runs", flags != null && flags.Count > 0);

        // 14. Controller Endpoints Verification
        var ctrlLotsResult = await qcController.GetLots(null, null, default);
        Assert("Check 17: QualityControlController GetLots returns HTTP 200 OkObjectResult", ctrlLotsResult is OkObjectResult);

        var ctrlLJResult = await qcController.GetLeveyJenningsData(gluLot.Id, default);
        Assert("Check 18: QualityControlController GetLeveyJenningsData returns HTTP 200 OkObjectResult", ctrlLJResult is OkObjectResult);

        var ctrlFlagsResult = await flagsController.GetQcFlags(default);
        Assert("Check 19: QcFlagsController GetQcFlags returns HTTP 200 OkObjectResult", ctrlFlagsResult is OkObjectResult);

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
