using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Infrastructure.Data;

namespace BMS.Application.Services;

public class DashboardService : IDashboardService
{
    private readonly BmsDbContext _context;

    public DashboardService(BmsDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardStatsDto> GetDashboardStatsAsync(string userId, string role)
    {
        await Task.Yield();
        var normalizedRole = (role ?? "Patient").Trim().ToLowerInvariant();

        var stats = new DashboardStatsDto
        {
            Role = role ?? "Patient",
            Timestamp = DateTime.UtcNow
        };

        var user = _context.Users.FirstOrDefault(u => u.Id == userId);
        stats.UserName = user?.FullName ?? (role ?? "Người dùng");

        switch (normalizedRole)
        {
            case "patient":
                stats.SummaryCards = new List<StatCardDto>
                {
                    new() { Key = "appointments", Title = "Lịch hẹn sắp tới", Value = "1", Icon = "calendar-check", ColorVariant = "primary", Subtitle = "Ngày mai lúc 08:30" },
                    new() { Key = "results", Title = "Kết quả đã có", Value = "3", Icon = "file-earmark-medical", ColorVariant = "success", Subtitle = "Bản PDF đã ký số" },
                    new() { Key = "pending", Title = "Đang xét nghiệm", Value = "1", Icon = "hourglass-split", ColorVariant = "warning", Subtitle = "Dự kiến có lúc 11:30" },
                    new() { Key = "notifications", Title = "Thông báo mới", Value = "2", Icon = "bell", ColorVariant = "info", Subtitle = "Nhắc nhịn ăn trước 8h" }
                };
                stats.PatientData = new PatientDashboardData
                {
                    UpcomingAppointmentsCount = 1,
                    ReadyResultsCount = 3,
                    PendingResultsCount = 1,
                    RecentAppointments = new List<DashboardAppointmentItemDto>
                    {
                        new()
                        {
                            Id = "APT-01",
                            AppointmentCode = "BMS-APT-20261012-01",
                            PatientName = stats.UserName,
                            PackageName = "Gói xét nghiệm Tổng quát Định kỳ 14 chỉ số",
                            AppointmentTime = DateTime.UtcNow.AddDays(1).Date.AddHours(8).AddMinutes(30),
                            Status = "Đã xác nhận",
                            DoctorName = "BS. Nguyễn Văn An"
                        }
                    },
                    RecentResults = new List<DashboardResultItemDto>
                    {
                        new()
                        {
                            TestOrderId = "ORD-099",
                            PackageName = "Sinh hóa máu cơ bản (Glucose, Ure, Creatinine)",
                            ReleasedAt = DateTime.UtcNow.AddDays(-2),
                            Status = "Đã duyệt",
                            HasAbnormalFlag = false
                        }
                    }
                };
                break;

            case "doctor":
                stats.SummaryCards = new List<StatCardDto>
                {
                    new() { Key = "today_appts", Title = "Lịch khám hôm nay", Value = "12", Icon = "person-lines-fill", ColorVariant = "primary", Subtitle = "4 bệnh nhân đang chờ" },
                    new() { Key = "pending_approvals", Title = "Chờ duyệt kết quả", Value = "5", Icon = "clipboard-check", ColorVariant = "warning", Subtitle = "2 ca có chỉ số cao" },
                    new() { Key = "reviewed", Title = "Đã hoàn thành duyệt", Value = "18", Icon = "check-circle", ColorVariant = "success", Subtitle = "Trong ngày hôm nay" },
                    new() { Key = "rerun", Title = "Yêu cầu chạy lại", Value = "1", Icon = "arrow-repeat", ColorVariant = "danger", Subtitle = "Chờ KTV xác nhận" }
                };
                stats.DoctorData = new DoctorDashboardData
                {
                    TodayAppointmentsCount = 12,
                    PendingApprovalsCount = 5,
                    ReviewedTodayCount = 18,
                    TodaySchedule = new List<DashboardAppointmentItemDto>
                    {
                        new()
                        {
                            Id = "APT-DOC-01",
                            AppointmentCode = "BMS-APT-101",
                            PatientName = "Trần Hoàng Long",
                            PackageName = "Bộ xét nghiệm Men Gan (AST, ALT, GGT)",
                            AppointmentTime = DateTime.UtcNow.AddHours(1),
                            Status = "Chờ khám"
                        },
                        new()
                        {
                            Id = "APT-DOC-02",
                            AppointmentCode = "BMS-APT-102",
                            PatientName = "Nguyễn Thị Mai",
                            PackageName = "Sinh hóa máu cơ bản",
                            AppointmentTime = DateTime.UtcNow.AddHours(2),
                            Status = "Chờ khám"
                        }
                    }
                };
                break;

            case "laboratoryuser":
            case "labuser":
                var waiting = _context.Samples.Count(s => s.Status == SampleStatus.Received);
                var processing = _context.Samples.Count(s => s.Status == SampleStatus.Processing);
                var completed = _context.Samples.Count(s => s.Status == SampleStatus.Completed);
                var rerun = _context.Samples.Count(s => s.Status == SampleStatus.RerunRequested);

                stats.SummaryCards = new List<StatCardDto>
                {
                    new() { Key = "queue_waiting", Title = "Mẫu chờ chạy", Value = waiting.ToString(), Icon = "inbox", ColorVariant = "primary", Subtitle = "Trong hàng đợi Lab" },
                    new() { Key = "in_progress", Title = "Đang xử lý trên máy", Value = processing.ToString(), Icon = "gear-wide-connected", ColorVariant = "warning", Subtitle = "Tại phòng 101, 102" },
                    new() { Key = "completed", Title = "Đã hoàn thành", Value = completed.ToString(), Icon = "check2-all", ColorVariant = "success", Subtitle = "Đã nhập chỉ số" },
                    new() { Key = "rerun_needed", Title = "Mẫu cần chạy lại", Value = rerun.ToString(), Icon = "exclamation-triangle", ColorVariant = "danger", Subtitle = "Ưu tiên kiểm tra" }
                };
                stats.LabUserData = new LabUserDashboardData
                {
                    QueueWaitingCount = waiting,
                    InProgressCount = processing,
                    CompletedTodayCount = completed,
                    RerunRequestedCount = rerun,
                    PrioritySamples = _context.Samples
                        .Where(s => s.Priority == SamplePriority.Urgent || s.Priority == SamplePriority.Stat || s.Status == SampleStatus.RerunRequested)
                        .Select(s => new LabQueueItemDto
                        {
                            Id = s.Id,
                            Barcode = s.Barcode,
                            PatientName = s.PatientName,
                            TestPackageName = s.TestPackageName,
                            SampleType = s.SampleType,
                            Status = s.Status.ToString(),
                            Priority = s.Priority.ToString(),
                            LabRoom = s.LabRoom,
                            CollectedAt = s.CollectedAt
                        }).ToList()
                };
                break;

            case "receptionist":
                stats.SummaryCards = new List<StatCardDto>
                {
                    new() { Key = "checkins", Title = "Bệnh nhân đã check-in", Value = "28", Icon = "qr-code-scan", ColorVariant = "success", Subtitle = "Hôm nay" },
                    new() { Key = "waiting_queue", Title = "Số đang chờ gọi", Value = "6", Icon = "people", ColorVariant = "primary", Subtitle = "Tại sảnh tiếp đón" },
                    new() { Key = "walkins", Title = "Khách vãng lai (Guest)", Value = "9", Icon = "person-plus", ColorVariant = "info", Subtitle = "Đã tạo hồ sơ" },
                    new() { Key = "collected", Title = "Tiền thu trong ngày", Value = "14,500,000 đ", Icon = "cash-coin", ColorVariant = "warning", Subtitle = "VNPay & Tiền mặt" }
                };
                stats.ReceptionistData = new ReceptionistDashboardData
                {
                    CheckInsToday = 28,
                    WaitingQueueCount = 6,
                    TotalWalkIns = 9,
                    PaymentsCollectedToday = 14500000m
                };
                break;

            case "manager":
            case "administrator":
            case "admin":
            default:
                stats.SummaryCards = new List<StatCardDto>
                {
                    new() { Key = "total_orders", Title = "Tổng đơn xét nghiệm", Value = "48", Icon = "clipboard-data", ColorVariant = "primary", Subtitle = "Tăng 12% so với hôm qua" },
                    new() { Key = "revenue", Title = "Doanh thu hôm nay", Value = "22,800,000 đ", Icon = "currency-dollar", ColorVariant = "success", Subtitle = "Đạt 114% chỉ tiêu ngày" },
                    new() { Key = "tat", Title = "Thời gian trả kết quả (TAT)", Value = "42 phút", Icon = "stopwatch", ColorVariant = "info", Subtitle = "Mục tiêu < 60 phút" },
                    new() { Key = "iot_alert", Title = "Cảnh báo nhiệt độ tủ IoT", Value = "Ổn định (3.8°C)", Icon = "thermometer-half", ColorVariant = "success", Subtitle = "Dải chuẩn: 2°C - 6°C" }
                };
                stats.ManagerData = new ManagerDashboardData
                {
                    TotalOrdersToday = 48,
                    RevenueToday = 22800000m,
                    AverageTatMinutes = 42,
                    ActiveAlertsCount = 0,
                    ColdStorageTemperature = 3.8
                };
                break;
        }

        return stats;
    }
}
