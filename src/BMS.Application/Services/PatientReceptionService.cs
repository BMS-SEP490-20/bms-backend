using BMS.Application.DTOs.Notifications;
using BMS.Application.DTOs.Reception;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Services;

/// <summary>
/// UC28 – Smart Queue & Clinical Reception Service.
/// Implements atomic concurrency locking, active doctor shift validation,
/// real-time notifications, and ISO 15189 queue lifecycle transitions.
/// </summary>
public class PatientReceptionService : IPatientReceptionService
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IQueueNotifier _queueNotifier;
    private readonly ILogger<PatientReceptionService> _logger;
    private readonly IQueueRepository? _queueRepository;
    private readonly ITestOrderRepository? _testOrderRepository;
    private readonly INotificationService? _notificationService;
    private readonly ITestPackageRepository? _packageRepository;

    public PatientReceptionService(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IQueueNotifier queueNotifier,
        ILogger<PatientReceptionService> logger,
        IQueueRepository? queueRepository = null,
        ITestOrderRepository? testOrderRepository = null,
        INotificationService? notificationService = null,
        ITestPackageRepository? packageRepository = null)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _queueNotifier = queueNotifier;
        _logger = logger;
        _queueRepository = queueRepository;
        _testOrderRepository = testOrderRepository;
        _notificationService = notificationService;
        _packageRepository = packageRepository;
    }

    private async Task<string?> ResolvePackageNameAsync(Appointment apt, CancellationToken ct)
    {
        var key = !string.IsNullOrWhiteSpace(apt.PackageCode) ? apt.PackageCode : apt.PackageId;
        if (_packageRepository != null && !string.IsNullOrWhiteSpace(key))
        {
            try
            {
                var pkg = await _packageRepository.GetByIdOrCodeAsync(key, includeInactive: true, ct);
                if (pkg != null && !string.IsNullOrWhiteSpace(pkg.PackageName))
                    return pkg.PackageName;
            }
            catch { /* non-fatal */ }
        }
        return !string.IsNullOrWhiteSpace(key) ? key : null;
    }

    private async Task<int?> ResolveEffectiveQueueNumberAsync(Appointment apt, CancellationToken ct)
    {
        if (apt.QueueNumber.HasValue && apt.QueueNumber.Value > 0)
            return apt.QueueNumber.Value;
        if (_queueRepository == null)
            return apt.QueueNumber;

        try
        {
            var day = BMS.Core.Constants.ClinicTime.LocalDateKey(DateTime.UtcNow);
            var tickets = await _queueRepository.GetByDateAsync(day, ct);
            var matched = tickets.FirstOrDefault(t =>
                (!string.IsNullOrWhiteSpace(apt.AppointmentCode) && string.Equals(t.PatientCode, apt.AppointmentCode, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(apt.Id) && (string.Equals(t.PatientCode, apt.Id, StringComparison.OrdinalIgnoreCase) || string.Equals(t.PatientId, apt.Id, StringComparison.OrdinalIgnoreCase))) ||
                (!string.IsNullOrWhiteSpace(apt.PatientId) && string.Equals(t.PatientId, apt.PatientId, StringComparison.OrdinalIgnoreCase)));
            if (matched != null && matched.Sequence > 0)
            {
                apt.QueueNumber = matched.Sequence;
                await _appointmentRepository.UpdateAsync(apt, ct);
                return matched.Sequence;
            }
        }
        catch
        {
            // Ignore fallback error
        }
        return apt.QueueNumber;
    }

    public async Task<QueueItemDto?> CallNextPatientAsync(CallNextPatientRequest request, CancellationToken ct = default)
    {
        // 1. Shift validation (BR-REC-02)
        var hasShift = await _appointmentRepository.HasActiveDoctorShiftTodayAsync(request.DoctorId, ct);
        if (!hasShift)
        {
            _logger.LogWarning("CallNext rejected: Doctor {DoctorId} has no active shift today", request.DoctorId);
            throw new InvalidOperationException("MSG-ERR-RECV-02: Bác sĩ chưa kích hoạt ca trực hôm nay hoặc ngoài giờ làm việc.");
        }

        // Room comes from ca phân công — không bắt BS chọn tay trên console.
        var roomId = request.RoomId;
        if (string.IsNullOrWhiteSpace(roomId))
        {
            var shifts = await _appointmentRepository.GetDoctorShiftsAsync(request.DoctorId, DateTime.UtcNow.Date, ct);
            roomId = shifts.FirstOrDefault(s =>
                    !string.Equals(s.Status, "Cancelled", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(s.Status, "Rejected", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(s.RoomId))
                ?.RoomId;
        }
        if (string.IsNullOrWhiteSpace(roomId))
            roomId = "ROOM-101";

        // 2. Atomic MongoDB queue lock
        var apt = await _appointmentRepository.CallNextPatientAtomicAsync(request.DoctorId, roomId, ct);
        if (apt == null)
        {
            _logger.LogInformation("Queue empty for doctor {DoctorId} / room {RoomId}", request.DoctorId, roomId);
            return null;
        }

        // 3. Resolve display details
        var doctor = await _appointmentRepository.GetDoctorAsync(request.DoctorId, ct);
        var doctorName = doctor?.FullName ?? "Bác sĩ phụ trách";

        var patient = await _patientRepository.GetByIdOrCodeAsync(apt.PatientId, ct);
        var patientName = patient?.FullName ?? apt.PatientId;
        var patientPhone = patient?.PhoneNumber;

        var effectiveQueueNumber = await ResolveEffectiveQueueNumberAsync(apt, ct);

        var dto = new QueueItemDto
        {
            AppointmentId = apt.Id,
            AppointmentCode = apt.AppointmentCode,
            QueueNumber = effectiveQueueNumber,
            PatientId = apt.PatientId,
            PatientName = patientName,
            PatientPhone = patientPhone,
            Status = apt.Status.ToString(),
            RoomId = apt.RoomId ?? roomId,
            DoctorId = request.DoctorId,
            DoctorName = doctorName,
            RecallCount = apt.RecallCount,
            ConsultationStartTime = apt.ConsultationStartTime,
            CreatedAt = apt.CreatedAt
        };

        // 4. SignalR Broadcast
        await _queueNotifier.NotifyPatientCalledAsync(roomId, new
        {
            queueNumber = dto.QueueNumberFormatted,
            room = roomId,
            name = dto.PatientName,
            appointmentId = dto.AppointmentId,
            doctorName = dto.DoctorName
        }, ct);

        await _queueNotifier.NotifyQueueUpdatedAsync(roomId, new { roomId }, ct);

        if (_notificationService != null && !string.IsNullOrWhiteSpace(dto.PatientId))
        {
            try
            {
                var docLabel = string.IsNullOrWhiteSpace(dto.DoctorName) ? "Bác sĩ" : $"BS. {dto.DoctorName}";
                await _notificationService.CreateNotificationAsync(new CreateNotificationDto
                {
                    UserId = dto.PatientId,
                    Role = RoleNames.Patient,
                    Title = $"Mời số {dto.QueueNumberFormatted} vào phòng khám",
                    Message = $"{docLabel} đang gọi số {dto.QueueNumberFormatted} tại phòng {roomId}. Vui lòng vào phòng khám.",
                    Type = "QueueCalled",
                    ReferenceId = dto.AppointmentId
                }, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send call-next notification"); }
        }

        _logger.LogInformation("Patient {PatientName} ({QueueNumber}) called to {RoomId} by {DoctorId}",
            dto.PatientName, dto.QueueNumberFormatted, roomId, request.DoctorId);

        return dto;
    }

    public async Task<QueueItemDto> RecallPatientAsync(RecallPatientRequest request, CancellationToken ct = default)
    {
        var apt = await _appointmentRepository.GetByIdAsync(request.AppointmentId, ct);
        if (apt == null)
        {
            throw new InvalidOperationException("MSG-ERR-APPT-06: Không tìm thấy cuộc hẹn.");
        }

        if (apt.RecallCount >= 3)
        {
            throw new InvalidOperationException("MSG-ERR-RECV-05: Đã gọi lại quá 3 lần. Vui lòng chuyển trạng thái vắng mặt (No-show).");
        }

        apt.RecallCount++;
        apt.RoomId = request.RoomId;
        apt.UpdatedAt = DateTime.UtcNow;
        await _appointmentRepository.UpdateAsync(apt, ct);

        var doctor = !string.IsNullOrEmpty(apt.DoctorId) ? await _appointmentRepository.GetDoctorAsync(apt.DoctorId, ct) : null;
        var patient = await _patientRepository.GetByIdOrCodeAsync(apt.PatientId, ct);

        var effectiveQueueNumber = await ResolveEffectiveQueueNumberAsync(apt, ct);

        var dto = new QueueItemDto
        {
            AppointmentId = apt.Id,
            AppointmentCode = apt.AppointmentCode,
            QueueNumber = effectiveQueueNumber,
            PatientId = apt.PatientId,
            PatientName = patient?.FullName ?? apt.PatientId,
            PatientPhone = patient?.PhoneNumber,
            Status = apt.Status.ToString(),
            RoomId = request.RoomId,
            DoctorId = apt.DoctorId,
            DoctorName = doctor?.FullName ?? "Bác sĩ phụ trách",
            RecallCount = apt.RecallCount,
            ConsultationStartTime = apt.ConsultationStartTime,
            CreatedAt = apt.CreatedAt
        };

        await _queueNotifier.NotifyPatientCalledAsync(request.RoomId, new
        {
            queueNumber = dto.QueueNumberFormatted,
            room = request.RoomId,
            name = dto.PatientName,
            appointmentId = dto.AppointmentId,
            doctorName = dto.DoctorName,
            isRecall = true,
            recallCount = dto.RecallCount
        }, ct);

        _logger.LogInformation("Patient {QueueNumber} recalled ({Count}x) to {RoomId}",
            dto.QueueNumberFormatted, dto.RecallCount, request.RoomId);

        return dto;
    }

    public async Task<bool> SkipPatientAsync(SkipPatientRequest request, CancellationToken ct = default)
    {
        var apt = await _appointmentRepository.GetByIdAsync(request.AppointmentId, ct);
        if (apt == null)
        {
            throw new InvalidOperationException("MSG-ERR-APPT-06: Không tìm thấy cuộc hẹn.");
        }

        apt.Status = AppointmentStatus.NoShow;
        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            apt.Notes = string.IsNullOrWhiteSpace(apt.Notes)
                ? $"No-show: {request.Reason}"
                : $"{apt.Notes}\nNo-show: {request.Reason}";
        }
        apt.UpdatedAt = DateTime.UtcNow;
        await _appointmentRepository.UpdateAsync(apt, ct);

        await _queueNotifier.NotifyQueueUpdatedAsync(apt.RoomId, new { roomId = apt.RoomId }, ct);

        _logger.LogInformation("Appointment {Id} skipped as No-show", apt.Id);
        return true;
    }

    public async Task<bool> CompletePatientAsync(CompletePatientRequest request, CancellationToken ct = default)
    {
        var apt = await _appointmentRepository.GetByIdAsync(request.AppointmentId, ct);
        if (apt == null)
        {
            throw new InvalidOperationException("MSG-ERR-APPT-06: Không tìm thấy cuộc hẹn.");
        }

        apt.Status = AppointmentStatus.Completed;
        apt.ConsultationEndTime = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            apt.Notes = string.IsNullOrWhiteSpace(apt.Notes)
                ? $"Complete note: {request.Notes}"
                : $"{apt.Notes}\nComplete note: {request.Notes}";
        }
        apt.UpdatedAt = DateTime.UtcNow;
        await _appointmentRepository.UpdateAsync(apt, ct);

        await _queueNotifier.NotifyQueueUpdatedAsync(apt.RoomId, new { roomId = apt.RoomId }, ct);

        if (_notificationService != null && !string.IsNullOrWhiteSpace(apt.PatientId))
        {
            try
            {
                await _notificationService.CreateNotificationAsync(new CreateNotificationDto
                {
                    UserId = apt.PatientId,
                    Role = RoleNames.Patient,
                    Title = "Hoàn tất lượt khám",
                    Message = $"Lượt khám {apt.AppointmentCode ?? apt.Id} của bạn đã hoàn tất.",
                    Type = "AppointmentCompleted",
                    ReferenceId = apt.Id
                }, ct);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to send completion notification"); }
        }

        _logger.LogInformation("Appointment {Id} consultation completed", apt.Id);
        return true;
    }

    public async Task<QueueStatusResponse> GetQueueStatusAsync(string? roomId, string? doctorId, CancellationToken ct = default)
    {
        var appointments = await _appointmentRepository.GetTodayQueueAppointmentsAsync(roomId, doctorId, ct);

        var doctorIds = appointments.Where(a => !string.IsNullOrEmpty(a.DoctorId)).Select(a => a.DoctorId!).Distinct().ToList();
        var doctors = (await _appointmentRepository.GetDoctorsByIdsAsync(doctorIds, ct)).ToDictionary(d => d.Id);

        var patientIds = appointments.Select(a => a.PatientId).Distinct().ToList();
        var patientDict = new Dictionary<string, Patient>();
        foreach (var pid in patientIds)
        {
            var p = await _patientRepository.GetByIdOrCodeAsync(pid, ct);
            if (p != null) patientDict[pid] = p;
        }

        // Prefer this doctor's InProgress in the selected room; if room filter empty, any own InProgress.
        // Also accept missing RoomId so reload after call-next still restores currentServing.
        var inProgress = appointments.FirstOrDefault(a =>
            a.Status == AppointmentStatus.InProgress
            && (string.IsNullOrWhiteSpace(doctorId) || a.DoctorId == doctorId)
            && (string.IsNullOrWhiteSpace(roomId)
                || string.IsNullOrWhiteSpace(a.RoomId)
                || a.RoomId == roomId));
        if (inProgress != null && _testOrderRepository != null)
        {
            try
            {
                var recentOrders = await _testOrderRepository.GetAllAsync(inProgress.PatientId, null, null, 20, ct);
                var hasDispatchedOrder = recentOrders.Any(o =>
                    o.Status != TestOrderStatus.Draft &&
                    o.Status != TestOrderStatus.Cancelled &&
                    (string.Equals(o.AppointmentId, inProgress.Id, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(o.AppointmentId, inProgress.AppointmentCode, StringComparison.OrdinalIgnoreCase) ||
                     (inProgress.ConsultationStartTime.HasValue && o.CreatedAt >= inProgress.ConsultationStartTime.Value.AddMinutes(-5))));
                if (hasDispatchedOrder)
                {
                    inProgress.Status = AppointmentStatus.Completed;
                    inProgress.ConsultationEndTime = DateTime.UtcNow;
                    inProgress.UpdatedAt = DateTime.UtcNow;
                    await _appointmentRepository.UpdateAsync(inProgress, ct);
                    appointments.Remove(inProgress);
                    inProgress = null;
                }
            }
            catch
            {
                // Ignore check failure
            }
        }
        QueueItemDto? currentServing = null;
        if (inProgress != null)
        {
            var effectiveQueueNum = await ResolveEffectiveQueueNumberAsync(inProgress, ct);
            var pkgName = await ResolvePackageNameAsync(inProgress, ct);
            doctors.TryGetValue(inProgress.DoctorId ?? string.Empty, out var doc);
            patientDict.TryGetValue(inProgress.PatientId, out var p);
            currentServing = new QueueItemDto
            {
                AppointmentId = inProgress.Id,
                AppointmentCode = inProgress.AppointmentCode,
                QueueNumber = effectiveQueueNum,
                PatientId = inProgress.PatientId,
                PatientName = p?.FullName ?? inProgress.PatientId,
                PatientPhone = p?.PhoneNumber,
                Status = inProgress.Status.ToString(),
                RoomId = inProgress.RoomId,
                DoctorId = inProgress.DoctorId,
                DoctorName = doc?.FullName ?? "Bác sĩ phụ trách",
                PackageId = inProgress.PackageId,
                PackageName = pkgName,
                RecallCount = inProgress.RecallCount,
                ConsultationStartTime = inProgress.ConsultationStartTime,
                CreatedAt = inProgress.CreatedAt
            };
        }

        var waiting = new List<QueueItemDto>();
        foreach (var a in appointments.Where(a => a.Status == AppointmentStatus.CheckedIn))
        {
            var effectiveQueueNum = await ResolveEffectiveQueueNumberAsync(a, ct);
            var pkgName = await ResolvePackageNameAsync(a, ct);
            doctors.TryGetValue(a.DoctorId ?? string.Empty, out var doc);
            patientDict.TryGetValue(a.PatientId, out var p);
            waiting.Add(new QueueItemDto
            {
                AppointmentId = a.Id,
                AppointmentCode = a.AppointmentCode,
                QueueNumber = effectiveQueueNum,
                PatientId = a.PatientId,
                PatientName = p?.FullName ?? a.PatientId,
                PatientPhone = p?.PhoneNumber,
                Status = a.Status.ToString(),
                RoomId = a.RoomId,
                DoctorId = a.DoctorId,
                DoctorName = doc?.FullName,
                PackageId = a.PackageId,
                PackageName = pkgName,
                RecallCount = a.RecallCount,
                ConsultationStartTime = a.ConsultationStartTime,
                CreatedAt = a.CreatedAt
            });
        }

        return new QueueStatusResponse
        {
            RoomId = roomId,
            WaitingCount = waiting.Count,
            InProgressCount = currentServing != null ? 1 : 0,
            CurrentServing = currentServing,
            WaitingList = waiting
        };
    }
}
