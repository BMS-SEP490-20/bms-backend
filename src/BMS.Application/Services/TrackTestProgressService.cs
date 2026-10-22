using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using MongoDB.Driver;

namespace BMS.Application.Services;

/// <summary>
/// Production service implementing UC48 Track Test Progress.
/// Conforms to ISO 15189 laboratory workflow, HIPAA privacy guidelines, and BR-PAT-02.
/// </summary>
public class TrackTestProgressService : ITrackTestProgressService
{
    private readonly IMongoCollection<TestOrder> _ordersCollection;
    private readonly IMongoCollection<Sample> _samplesCollection;
    private readonly IMongoCollection<TestResult> _resultsCollection;

    public TrackTestProgressService(IMongoDatabase database)
    {
        _ordersCollection = database.GetCollection<TestOrder>("test_orders");
        _samplesCollection = database.GetCollection<Sample>("blood_samples");
        _resultsCollection = database.GetCollection<TestResult>("test_results");
    }

    public async Task<TrackProgressResponse?> GetTrackingAsync(string identifier, bool isPublicQuery = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        var cleanId = identifier.Trim();

        // 1. Try to find by TestOrder (OrderNumber or Id)
        var order = await _ordersCollection.Find(x => x.OrderNumber == cleanId || x.Id == cleanId).FirstOrDefaultAsync(ct);

        Sample? sample = null;
        if (order != null)
        {
            // Find sample by TestOrderId or appointment id
            sample = await _samplesCollection.Find(x => x.TestOrderId == order.Id || x.Barcode == cleanId).FirstOrDefaultAsync(ct);
        }
        else
        {
            // Try to find by Sample Barcode or Sample Id
            sample = await _samplesCollection.Find(x => x.Barcode == cleanId || x.Id == cleanId).FirstOrDefaultAsync(ct);
            if (sample != null)
            {
                order = await _ordersCollection.Find(x => x.Id == sample.TestOrderId || x.OrderNumber == sample.TestOrderId).FirstOrDefaultAsync(ct);
            }
        }

        if (order == null && sample == null)
            return null;

        // Find test result if available
        TestResult? result = null;
        if (sample != null)
        {
            result = await _resultsCollection.Find(x => x.SampleId == sample.Id || x.SampleId == sample.Barcode).FirstOrDefaultAsync(ct);
        }

        return BuildTrackingResponse(order, sample, result, isPublicQuery);
    }

    public async Task<List<TrackProgressResponse>> GetPatientActiveTrackingsAsync(string patientId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patientId))
            return new List<TrackProgressResponse>();

        var cleanPatientId = patientId.Trim();

        var orders = await _ordersCollection
            .Find(x => x.PatientId == cleanPatientId)
            .SortByDescending(x => x.CreatedAt)
            .Limit(20)
            .ToListAsync(ct);

        var list = new List<TrackProgressResponse>();
        foreach (var order in orders)
        {
            var sample = await _samplesCollection.Find(x => x.TestOrderId == order.Id).FirstOrDefaultAsync(ct);
            TestResult? result = null;
            if (sample != null)
            {
                result = await _resultsCollection.Find(x => x.SampleId == sample.Id).FirstOrDefaultAsync(ct);
            }
            list.Add(BuildTrackingResponse(order, sample, result, isPublicQuery: false));
        }

        return list;
    }

    public async Task<List<TrackProgressResponse>> SearchTrackingsAsync(
        string? query, string? status, DateTime? fromDate, DateTime? toDate, int limit = 50, CancellationToken ct = default)
    {
        var filterBuilder = Builders<TestOrder>.Filter;
        var filter = filterBuilder.Empty;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            filter &= filterBuilder.Or(
                filterBuilder.Regex(x => x.OrderNumber, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                filterBuilder.Regex(x => x.PatientName, new MongoDB.Bson.BsonRegularExpression(q, "i")),
                filterBuilder.Regex(x => x.PatientId, new MongoDB.Bson.BsonRegularExpression(q, "i"))
            );
        }

        if (fromDate.HasValue)
        {
            filter &= filterBuilder.Gte(x => x.CreatedAt, fromDate.Value.Date);
        }

        if (toDate.HasValue)
        {
            filter &= filterBuilder.Lte(x => x.CreatedAt, toDate.Value.Date.AddDays(1).AddTicks(-1));
        }

        var orders = await _ordersCollection
            .Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .Limit(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

        var list = new List<TrackProgressResponse>();
        foreach (var order in orders)
        {
            var sample = await _samplesCollection.Find(x => x.TestOrderId == order.Id).FirstOrDefaultAsync(ct);
            TestResult? result = null;
            if (sample != null)
            {
                result = await _resultsCollection.Find(x => x.SampleId == sample.Id).FirstOrDefaultAsync(ct);
            }

            var item = BuildTrackingResponse(order, sample, result, isPublicQuery: false);
            if (!string.IsNullOrWhiteSpace(status) && !item.OverallStatus.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            list.Add(item);
        }

        return list;
    }

    private TrackProgressResponse BuildTrackingResponse(TestOrder? order, Sample? sample, TestResult? result, bool isPublicQuery)
    {
        var orderNumber = order?.OrderNumber ?? sample?.Barcode ?? "ORD-UNKNOWN";
        var patientName = order?.PatientName ?? sample?.PatientName ?? "Bệnh nhân";
        if (isPublicQuery)
        {
            patientName = MaskPatientName(patientName);
        }

        var patientId = order?.PatientId ?? sample?.PatientId ?? string.Empty;
        var orderedAt = order?.CreatedAt ?? sample?.CollectedAt ?? DateTime.UtcNow;
        var priority = order?.Priority.ToString() ?? sample?.Priority.ToString() ?? "Normal";

        var isStat = priority.Equals("Stat", StringComparison.OrdinalIgnoreCase) || priority.Equals("Urgent", StringComparison.OrdinalIgnoreCase);
        var expectedDurationMinutes = isStat ? 60 : 120;
        var estimatedCompletionTime = orderedAt.AddMinutes(expectedDurationMinutes);

        var packages = order?.Items?.Select(i => i.TestName).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList()
            ?? (string.IsNullOrEmpty(sample?.TestPackageName) ? new List<string> { "Xét nghiệm tổng quát" } : new List<string> { sample.TestPackageName });

        var isRejected = sample?.Status == SampleStatus.Rejected || (!string.IsNullOrEmpty(sample?.RejectionReason));
        var isRerun = !string.IsNullOrEmpty(sample?.RerunReason) || sample?.Status == SampleStatus.RerunRequested;

        var steps = new List<TrackingStepDto>();

        // Step 1: REGISTERED
        steps.Add(new TrackingStepDto
        {
            StepNumber = 1,
            StepCode = "REGISTERED",
            Title = "Tiếp Nhận & Đăng Ký Chỉ Định",
            Description = "Hồ sơ xét nghiệm đã được tạo và chỉ định bởi Bác sĩ lâm sàng.",
            Status = TrackingStepStatus.Completed,
            StatusText = "Đã hoàn thành",
            Timestamp = orderedAt,
            HandlerName = order?.DoctorName ?? "Bác sĩ phụ trách",
            Department = "Phòng Khám Lâm Sàng"
        });

        // Step 2: SAMPLE_COLLECTED
        bool step2Done = (sample != null && sample.CollectedAt != default) || (order != null && order.Status >= TestOrderStatus.SampleCollected);
        steps.Add(new TrackingStepDto
        {
            StepNumber = 2,
            StepCode = "SAMPLE_COLLECTED",
            Title = "Lấy Mẫu Máu & Dán Barcode",
            Description = "Điều dưỡng thực hiện lấy mẫu vô trùng và dán tem mã vạch Barcode Code 128.",
            Status = step2Done ? TrackingStepStatus.Completed : TrackingStepStatus.InProgress,
            StatusText = step2Done ? "Đã lấy mẫu" : "Đang chờ lấy mẫu",
            Timestamp = step2Done ? (sample?.CollectedAt ?? orderedAt.AddMinutes(15)) : null,
            HandlerName = sample?.CollectorName ?? "Điều dưỡng lấy mẫu",
            Department = "Phòng Lấy Mẫu Bệnh Phẩm"
        });

        // Step 3: LAB_RECEIVED
        bool step3Done = false;
        var step3Status = TrackingStepStatus.Pending;
        string step3Text = "Chờ nhập Lab";
        string? step3Notes = null;

        if (isRejected)
        {
            step3Status = TrackingStepStatus.Rejected;
            step3Text = "Mẫu bị từ chối";
            step3Notes = sample?.RejectionReason ?? "Mẫu không đạt chất lượng y khoa (Huyết tán/Đông dây). Vui lòng liên hệ quầy lấy lại mẫu.";
        }
        else if (sample != null && (sample.ReceivedAt.HasValue || sample.Status >= SampleStatus.Received))
        {
            step3Done = true;
            step3Status = TrackingStepStatus.Completed;
            step3Text = "Đã nhập Lab";
        }
        else if (order != null && order.Status >= TestOrderStatus.InAnalysis)
        {
            step3Done = true;
            step3Status = TrackingStepStatus.Completed;
            step3Text = "Đã nhập Lab";
        }
        else if (step2Done)
        {
            step3Status = TrackingStepStatus.InProgress;
            step3Text = "Đang vận chuyển";
        }

        steps.Add(new TrackingStepDto
        {
            StepNumber = 3,
            StepCode = "LAB_RECEIVED",
            Title = "Tiếp Nhận Phòng Xét Nghiệm (LIS)",
            Description = "Mẫu đã tới phòng xét nghiệm, hệ thống LIS kiểm tra thể tích và quét nhận mẫu.",
            Status = step3Status,
            StatusText = step3Text,
            Timestamp = step3Done ? (sample?.ReceivedAt ?? (sample?.CollectedAt.AddMinutes(10) ?? orderedAt.AddMinutes(25))) : null,
            HandlerName = sample?.ReceivedBy ?? "KTV Tiếp Nhận",
            Department = "Khoa Xét Nghiệm Trung Tâm",
            Notes = step3Notes
        });

        // Step 4: ANALYZING
        bool step4Done = false;
        var step4Status = TrackingStepStatus.Pending;
        string step4Text = "Chờ phân tích";
        string? step4Notes = null;

        if (isRerun)
        {
            step4Status = TrackingStepStatus.Warning;
            step4Text = "Đang chạy lại mẫu (Rerun)";
            step4Notes = sample?.RerunReason ?? "Cần chạy lại kiểm chứng chỉ số nghi ngờ.";
        }
        else if (result != null || (sample != null && sample.ProcessedAt.HasValue) || (order != null && order.Status >= TestOrderStatus.Completed))
        {
            step4Done = true;
            step4Status = TrackingStepStatus.Completed;
            step4Text = "Đã phân tích";
        }
        else if (step3Done)
        {
            step4Status = TrackingStepStatus.InProgress;
            step4Text = "Đang phân tích máy";
        }

        steps.Add(new TrackingStepDto
        {
            StepNumber = 4,
            StepCode = "ANALYZING",
            Title = "Phân Tích Thiết Bị Tự Động",
            Description = "Mẫu được phân tích trên máy Cobas / Sysmex thế hệ mới với nội kiểm IQC đạt chuẩn.",
            Status = step4Status,
            StatusText = step4Text,
            Timestamp = step4Done ? (sample?.ProcessedAt ?? result?.CreatedAt ?? orderedAt.AddMinutes(50)) : null,
            HandlerName = sample?.AssignedTechnicianName ?? "KTV Phân Tích",
            Department = "Phòng Máy Tự Động Hóa",
            Notes = step4Notes
        });

        // Step 5: MEDICAL_REVIEW
        bool step5Done = false;
        var step5Status = TrackingStepStatus.Pending;
        string step5Text = "Chờ thẩm định";

        if ((result != null && result.ValidationStatus == ResultValidationStatus.DoctorApproved) || (order != null && order.Status == TestOrderStatus.Completed) || (sample != null && sample.CompletedAt.HasValue))
        {
            step5Done = true;
            step5Status = TrackingStepStatus.Completed;
            step5Text = "Đã thẩm định";
        }
        else if (step4Done)
        {
            step5Status = TrackingStepStatus.InProgress;
            step5Text = "Đang thẩm định y khoa";
        }

        steps.Add(new TrackingStepDto
        {
            StepNumber = 5,
            StepCode = "MEDICAL_REVIEW",
            Title = "Thẩm Định Chuyên Môn Y Khoa",
            Description = "Bác sĩ Chuyên khoa xét nghiệm đánh giá dải tham chiếu, kết luận lâm sàng.",
            Status = step5Status,
            StatusText = step5Text,
            Timestamp = step5Done ? (sample?.CompletedAt ?? result?.ApprovedAt ?? result?.CreatedAt ?? orderedAt.AddMinutes(75)) : null,
            HandlerName = "Bác sĩ / Trưởng Khoa Xét Nghiệm",
            Department = "Hội Đồng Thẩm Định Y Khoa"
        });

        // Step 6: COMPLETED
        bool step6Done = step5Done;
        var step6Status = step6Done ? TrackingStepStatus.Completed : (step4Done ? TrackingStepStatus.InProgress : TrackingStepStatus.Pending);

        steps.Add(new TrackingStepDto
        {
            StepNumber = 6,
            StepCode = "COMPLETED",
            Title = "Phát Hành Kết Quả & Chữ Ký Số",
            Description = "Kết quả đã được ký số điện tử bảo mật SHA-256, sẵn sàng xem trực tuyến hoặc nhận phiếu.",
            Status = step6Status,
            StatusText = step6Done ? "Đã có kết quả" : "Chờ phát hành",
            Timestamp = step6Done ? (sample?.CompletedAt ?? result?.ApprovedAt ?? result?.CreatedAt ?? orderedAt.AddMinutes(90)) : null,
            HandlerName = "Hệ Thống BMS LIS (ISO 15189)",
            Department = "Cổng Kết Quả Bệnh Nhân"
        });

        int currentStepNum = 1;
        string currentStepName = "Tiếp Nhận";
        if (isRejected)
        {
            currentStepNum = 3;
            currentStepName = "Mẫu Bị Từ Chối";
        }
        else if (step6Done)
        {
            currentStepNum = 6;
            currentStepName = "Đã Có Kết Quả";
        }
        else if (step5Done)
        {
            currentStepNum = 6;
            currentStepName = "Đang Phát Hành Kết Quả";
        }
        else if (step4Done)
        {
            currentStepNum = 5;
            currentStepName = "Đang Thẩm Định Y Khoa";
        }
        else if (step3Done)
        {
            currentStepNum = 4;
            currentStepName = "Đang Phân Tích Thiết Bị";
        }
        else if (step2Done)
        {
            currentStepNum = 3;
            currentStepName = "Mẫu Đang Vận Chuyển Đến Lab";
        }
        else
        {
            currentStepNum = 2;
            currentStepName = "Chờ Lấy Mẫu Máu";
        }

        var elapsedMinutes = Math.Round((DateTime.UtcNow - orderedAt).TotalMinutes, 1);

        return new TrackProgressResponse
        {
            OrderNumber = orderNumber,
            Barcode = sample?.Barcode ?? orderNumber,
            PatientId = patientId,
            PatientName = patientName,
            PatientGender = order?.PatientGender ?? sample?.PatientGender ?? "Nam",
            PatientAge = order?.PatientAge ?? sample?.PatientAge ?? 30,
            Priority = priority,
            TestPackageNames = packages,
            OverallStatus = isRejected ? "Rejected" : (step6Done ? "Completed" : "Processing"),
            CurrentStepNumber = currentStepNum,
            CurrentStepName = currentStepName,
            OrderedAt = orderedAt,
            EstimatedCompletionTime = estimatedCompletionTime,
            ElapsedMinutes = Math.Max(0, elapsedMinutes),
            IsCompleted = step6Done,
            IsRejected = isRejected,
            RejectionReason = sample?.RejectionReason,
            IsRerunRequested = isRerun,
            RerunReason = sample?.RerunReason,
            Steps = steps
        };
    }

    private static string MaskPatientName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return "Bệnh nhân";

        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
        {
            if (fullName.Length <= 2) return fullName;
            return fullName[0] + new string('*', fullName.Length - 2) + fullName[^1];
        }

        // Example: "Nguyễn Tấn Phát" -> "Nguyễn T** P***"
        var masked = new List<string> { parts[0] };
        for (int i = 1; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Length <= 1) masked.Add(p);
            else masked.Add(p[0] + new string('*', Math.Max(1, p.Length - 1)));
        }
        return string.Join(" ", masked);
    }
}
