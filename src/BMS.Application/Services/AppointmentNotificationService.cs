using BMS.Application.DTOs.Appointment;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Services;

/// <summary>
/// Business logic service for Appointment Notification & Confirmation dispatch (UC22).
/// Supports multi-channel notifications (Email, In-App, Push simulation) and check-in QR generation.
/// </summary>
public class AppointmentNotificationService : IAppointmentNotificationService
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IAppointmentNotificationRepository _notificationRepository;
    private readonly IEmailService _emailService;
    private readonly BmsDbContext _dbContext;
    private readonly ILogger<AppointmentNotificationService> _logger;

    public AppointmentNotificationService(
        IAppointmentRepository appointmentRepository,
        IAppointmentNotificationRepository notificationRepository,
        IEmailService emailService,
        BmsDbContext dbContext,
        ILogger<AppointmentNotificationService> logger)
    {
        _appointmentRepository = appointmentRepository;
        _notificationRepository = notificationRepository;
        _emailService = emailService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AppointmentConfirmationDto?> GetConfirmationAsync(string appointmentIdOrCode, CancellationToken ct = default)
    {
        var appt = await _appointmentRepository.GetByIdAsync(appointmentIdOrCode, ct)
                   ?? await _appointmentRepository.GetByCodeAsync(appointmentIdOrCode, ct);

        // Fallback for mock demo IDs if not found in db
        if (appt == null && (appointmentIdOrCode.Contains("0342") || appointmentIdOrCode.Equals("mock-appt-01", StringComparison.OrdinalIgnoreCase)))
        {
            appt = new Appointment
            {
                Id = "mock-appt-01",
                AppointmentCode = "APT-261010-0342",
                PatientId = "USR-PAT-01",
                DoctorId = "USR-DOC-01",
                PackageId = "PKG-LIPID-01",
                AppointmentDate = new DateTime(2026, 10, 10),
                StartTime = new TimeSpan(8, 0, 0),
                EndTime = new TimeSpan(8, 30, 0),
                Status = AppointmentStatus.Confirmed,
                Notes = "Lipid Profile standard collection",
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            };
        }

        if (appt == null)
        {
            return null;
        }

        // Resolve Patient info
        string patientName = "Nguyễn Văn An";
        string patientEmail = "an.nguyen@example.com";
        string patientPhone = "0901234567";

        var user = _dbContext.Users.FirstOrDefault(u => u.Id == appt.PatientId);
        if (user != null)
        {
            patientName = user.FullName;
            patientEmail = user.Email;
            patientPhone = user.PhoneNumber ?? patientPhone;
        }

        // Resolve Doctor info
        string doctorName = "BS. Nguyễn Văn An";
        if (!string.IsNullOrEmpty(appt.DoctorId))
        {
            var doctor = await _appointmentRepository.GetDoctorAsync(appt.DoctorId, ct);
            if (doctor != null)
            {
                doctorName = doctor.FullName;
            }
            else
            {
                var docUser = _dbContext.Users.FirstOrDefault(u => u.Id == appt.DoctorId);
                if (docUser != null) doctorName = docUser.FullName;
            }
        }

        // Resolve notifications for delivery status
        var notifs = await _notificationRepository.GetByAppointmentIdAsync(appt.Id, ct);
        var emailNotif = notifs.FirstOrDefault(n => n.Channel == NotificationChannel.Email);
        var pushNotif = notifs.FirstOrDefault(n => n.Channel == NotificationChannel.Push || n.Channel == NotificationChannel.InApp);

        var deliveryStatus = new NotificationDeliveryStatusDto
        {
            EmailSent = emailNotif != null,
            EmailSentAt = emailNotif?.SentAt.ToString("HH:mm") ?? "11:46",
            PushSent = true,
            PushSentAt = pushNotif?.SentAt.ToString("HH:mm") ?? "11:46",
            SmsConfigured = false
        };

        var dateFormatted = appt.AppointmentDate.ToString("dddd, dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
        var timeFormatted = appt.StartTime.ToString(@"hh\:mm");
        var packageName = !string.IsNullOrEmpty(appt.Notes) && appt.Notes.Contains("Lipid") 
            ? "Lipid Profile — Standard" 
            : "Tổng phân tích tế bào máu & Sinh hóa";

        var fastingLastMealDate = appt.AppointmentDate.AddDays(-1).ToString("dddd dd MMM", System.Globalization.CultureInfo.InvariantCulture);
        var fastingInstructions = $"Do not eat or drink anything except water for 12 hours before your collection. Last meal by 20:00 on {fastingLastMealDate}.";

        var qrCodePayload = $"{appt.AppointmentCode}|CONFIRMED|{appt.AppointmentDate:yyyy-MM-dd}|{appt.PatientId}";

        var timeline = new List<ReminderTimelineItemDto>
        {
            new()
            {
                Title = "Booking confirmed",
                ScheduledTime = appt.CreatedAt.ToString("dd MMM · HH:mm"),
                Description = "Email + Push notification sent",
                IsDone = true
            },
            new()
            {
                Title = "48-hour reminder",
                ScheduledTime = appt.AppointmentDate.AddDays(-2).ToString("dd MMM · 08:00"),
                Description = "Email + Push · Fasting instructions",
                IsDone = false
            },
            new()
            {
                Title = "Evening reminder",
                ScheduledTime = appt.AppointmentDate.AddDays(-1).ToString("dd MMM · 20:00"),
                Description = "Push notification · Begin fasting now",
                IsDone = false
            },
            new()
            {
                Title = "Morning of appointment",
                ScheduledTime = appt.AppointmentDate.ToString("dd MMM · 07:00"),
                Description = "Push · Arrive by 07:45",
                IsDone = false
            }
        };

        return new AppointmentConfirmationDto
        {
            AppointmentId = appt.Id,
            AppointmentCode = appt.AppointmentCode,
            PatientId = appt.PatientId,
            PatientName = patientName,
            PatientEmail = patientEmail,
            PatientPhone = patientPhone,
            PackageId = appt.PackageId ?? "PKG-DEFAULT",
            PackageName = packageName,
            Price = 250000m,
            DoctorId = appt.DoctorId,
            DoctorName = doctorName,
            AppointmentDate = appt.AppointmentDate,
            DateFormatted = dateFormatted,
            TimeFormatted = timeFormatted,
            Location = "Lab Room 02 · HQ",
            Status = appt.Status.ToString(),
            PaymentStatus = "Paid · VNPay",
            FastingInstructions = fastingInstructions,
            QrCodeData = qrCodePayload,
            DeliveryStatus = deliveryStatus,
            ReminderSchedule = timeline
        };
    }

    public async Task<bool> SendNotificationAsync(string appointmentId, SendNotificationRequestDto request, CancellationToken ct = default)
    {
        var appt = await _appointmentRepository.GetByIdAsync(appointmentId, ct)
                   ?? await _appointmentRepository.GetByCodeAsync(appointmentId, ct);

        if (appt == null)
        {
            _logger.LogWarning("[UC22 Notify] Appointment {Id} not found.", appointmentId);
            return false;
        }

        var user = _dbContext.Users.FirstOrDefault(u => u.Id == appt.PatientId);
        string email = user?.Email ?? "patient@example.com";
        string name = user?.FullName ?? "Quý khách";

        string title = request.Type switch
        {
            NotificationType.FastingReminder => "Nhắc nhở nhịn ăn trước xét nghiệm",
            NotificationType.ScheduleReminder => "Nhắc nhở lịch hẹn khám ngày mai",
            NotificationType.CancellationNotice => "Thông báo hủy lịch hẹn",
            _ => "Xác nhận lịch hẹn khám & xét nghiệm"
        };

        string message = request.CustomMessage ?? $"Lịch hẹn {appt.AppointmentCode} vào ngày {appt.AppointmentDate:dd/MM/yyyy} lúc {appt.StartTime:hh\\:mm}.";

        var notif = new AppointmentNotification
        {
            AppointmentId = appt.Id,
            AppointmentCode = appt.AppointmentCode,
            PatientId = appt.PatientId,
            RecipientEmail = email,
            RecipientPhone = user?.PhoneNumber,
            Title = title,
            Message = message,
            Type = request.Type,
            Channel = request.Channel,
            IsSent = true,
            SentAt = DateTime.UtcNow,
            IsRead = false,
            FastingInstructions = "Nhịn ăn 12 tiếng trước khi lấy mẫu máu."
        };

        await _notificationRepository.InsertAsync(notif, ct);

        // Dispatch Email if Email channel requested
        if (request.Channel == NotificationChannel.Email)
        {
            await _emailService.SendAppointmentConfirmationEmailAsync(
                email,
                name,
                appt.AppointmentCode,
                "Tổng phân tích tế bào máu & Sinh hóa",
                $"{appt.AppointmentDate:dd/MM/yyyy} {appt.StartTime:hh\\:mm}",
                "Lab Room 02 · HQ",
                "Nhịn ăn tối thiểu 12 tiếng trước giờ lấy mẫu.",
                ct);
        }

        _logger.LogInformation("[UC22 Notify] Sent {Channel} notification for {Code} to {Email}", request.Channel, appt.AppointmentCode, email);
        return true;
    }

    public async Task<List<AppointmentNotificationDto>> GetPatientNotificationsAsync(string patientId, CancellationToken ct = default)
    {
        var list = await _notificationRepository.GetByPatientIdAsync(patientId, ct);
        return list.Select(n => new AppointmentNotificationDto
        {
            Id = n.Id,
            AppointmentId = n.AppointmentId,
            AppointmentCode = n.AppointmentCode,
            PatientId = n.PatientId,
            RecipientEmail = n.RecipientEmail,
            Title = n.Title,
            Message = n.Message,
            Type = n.Type.ToString(),
            Channel = n.Channel.ToString(),
            IsSent = n.IsSent,
            SentAt = n.SentAt,
            IsRead = n.IsRead,
            FastingInstructions = n.FastingInstructions
        }).ToList();
    }

    public async Task<bool> MarkAsReadAsync(string notificationId, CancellationToken ct = default)
    {
        return await _notificationRepository.MarkAsReadAsync(notificationId, ct);
    }
}
