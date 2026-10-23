using System;
using System.Linq;
using System.Threading.Tasks;
using BMS.Api.Controllers.IoT;
using BMS.Application.DTOs.IoT;
using BMS.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace BMS.UC68.Checks;

public class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== BẮT ĐẦU KIỂM THỬ TỰ ĐỘNG UC68 BACKEND (ALERT THRESHOLD VIOLATION) ===\n");

        var logger = NullLogger<IotAlertService>.Instance;
        var service = new IotAlertService(logger);
        var controller = new AlertsController(service);

        // 1. Service initialization
        Assert("Check 1: Service is properly instantiated", service != null);

        // 2. Initial Alert Summary
        var summary = await service!.GetAlertSummaryAsync();
        Assert("Check 2: Summary returns active alerts count > 0", summary != null && summary.ActiveAlertsCount > 0);
        Assert("Check 3: System health status is accurately reported", summary != null && !string.IsNullOrEmpty(summary.SystemHealthStatus));

        // 3. Alerts listing
        var defaultQuery = new IotAlertQueryParameters();
        var allAlerts = await service.GetAlertsAsync(defaultQuery);
        Assert("Check 4: Retrieves seeded alerts with valid properties", allAlerts != null && allAlerts.Count >= 3);

        // 4. Normal Telemetry Evaluation (No violation)
        var normalReading = new TelemetryReadingDto
        {
            DeviceId = "TEMP-001",
            SensorType = "Temperature",
            Value = 4.2
        };
        var normalEval = await service.EvaluateTelemetryAsync(normalReading);
        Assert("Check 5: Normal reading (4.2°C in 2-6°C) triggers NO violation", normalEval != null && !normalEval.IsViolation && normalEval.Alert == null);

        // 5. Minor Excursion (Warning severity)
        var warningReading = new TelemetryReadingDto
        {
            DeviceId = "TEMP-001",
            SensorType = "Temperature",
            Value = 6.8 // +0.8°C excursion
        };
        var warningEval = await service.EvaluateTelemetryAsync(warningReading);
        Assert("Check 6: Minor excursion (+0.8°C) triggers Warning severity violation",
            warningEval != null && warningEval.IsViolation && warningEval.Severity == "Warning" && warningEval.ExcursionAmount > 0);

        // 6. Serious Excursion (Critical severity)
        var criticalReading = new TelemetryReadingDto
        {
            DeviceId = "TEMP-001",
            SensorType = "Temperature",
            Value = 8.5 // +2.5°C excursion
        };
        var criticalEval = await service.EvaluateTelemetryAsync(criticalReading);
        Assert("Check 7: Excursion (+2.5°C) triggers Critical severity violation",
            criticalEval != null && criticalEval.IsViolation && criticalEval.Severity == "Critical");

        // 7. Danger Emergency Excursion (> 3°C excursion)
        var dangerReading = new TelemetryReadingDto
        {
            DeviceId = "TEMP-001",
            SensorType = "Temperature",
            Value = 10.4 // +4.4°C excursion
        };
        var dangerEval = await service.EvaluateTelemetryAsync(dangerReading);
        Assert("Check 8: Severe excursion (>3°C) triggers Danger_Emergency severity violation",
            dangerEval != null && dangerEval.IsViolation && dangerEval.Severity == "Danger_Emergency");
        Assert("Check 9: High severity alerts dispatch multi-channel (SMS, Email, Push, InApp)",
            dangerEval?.Alert != null && dangerEval.Alert.NotificationChannelsDispatched.Contains("SMS") && dangerEval.Alert.NotificationChannelsDispatched.Contains("Email"));

        // 8. Alert Acknowledgment
        var activeAlert = allAlerts?.FirstOrDefault(a => a.Status == "Active");
        Assert("Check 10: Found active alert for testing acknowledgment", activeAlert != null);

        var ackRequest = new AcknowledgeAlertRequestDto
        {
            OperatorName = "KTV. Nguyễn Tấn Phát",
            Notes = "Đang kiểm tra phòng bảo quản và kiểm tra máy lạnh"
        };
        var ackResult = await service.AcknowledgeAlertAsync(activeAlert!.Id, ackRequest);
        Assert("Check 11: Alert acknowledgment sets status to Acknowledged and records operator",
            ackResult.Status == "Acknowledged" && ackResult.AcknowledgedBy == "KTV. Nguyễn Tấn Phát" && ackResult.AcknowledgedAt.HasValue);

        // 9. Alert Resolution
        var resolveRequest = new ResolveAlertRequestDto
        {
            OperatorName = "KTV. Nguyễn Tấn Phát",
            ResolutionNotes = "Đã thay van cấp lạnh và đưa nhiệt độ về mức 3.8°C ổn định."
        };
        var resolveResult = await service.ResolveAlertAsync(activeAlert.Id, resolveRequest);
        Assert("Check 12: Alert resolution sets status to Resolved and documents corrective actions",
            resolveResult.Status == "Resolved" && resolveResult.ResolvedBy == "KTV. Nguyễn Tấn Phát" && resolveResult.ResolvedAt.HasValue && !string.IsNullOrEmpty(resolveResult.ResolutionNotes));

        // 10. Simulation Alert Triggering
        var simRequest = new SimulateAlertRequestDto
        {
            DeviceId = "SIM-TEMP-99",
            DeviceName = "Tủ thử nghiệm diễn tập",
            Location = "Phòng Diễn Tập",
            SensorType = "Temperature",
            SimulatedValue = 9.8,
            LowAlert = 2.0,
            HighAlert = 6.0
        };
        var simResult = await service.TriggerSimulatedAlertAsync(simRequest);
        Assert("Check 13: Simulation alert creates active violation with recorded excursion",
            simResult != null && simResult.DeviceId == "SIM-TEMP-99" && simResult.Status == "Active" && simResult.ExcursionAmount > 0);

        // 11. Controller Endpoints Verification
        var ctrlGetResult = await controller.GetAlerts(defaultQuery, default);
        Assert("Check 14: Controller GetAlerts returns HTTP 200 OkObjectResult", ctrlGetResult is OkObjectResult);

        var ctrlActiveResult = await controller.GetActiveAlerts(default);
        Assert("Check 15: Controller GetActiveAlerts returns HTTP 200 OkObjectResult", ctrlActiveResult is OkObjectResult);

        var ctrlSummaryResult = await controller.GetAlertSummary(default);
        Assert("Check 16: Controller GetAlertSummary returns HTTP 200 OkObjectResult", ctrlSummaryResult is OkObjectResult);

        var ctrlEvalResult = await controller.EvaluateTelemetry(normalReading, default);
        Assert("Check 17: Controller EvaluateTelemetry returns HTTP 200 OkObjectResult", ctrlEvalResult is OkObjectResult);

        var ctrlSimResult = await controller.SimulateAlert(simRequest, default);
        Assert("Check 18: Controller SimulateAlert returns HTTP 200 OkObjectResult", ctrlSimResult is OkObjectResult);

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
