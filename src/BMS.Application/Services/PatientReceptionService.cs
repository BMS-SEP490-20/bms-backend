using BMS.Application.DTOs.Reception;
using BMS.Application.Interfaces;
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

    public PatientReceptionService(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IQueueNotifier queueNotifier,
        ILogger<PatientReceptionService> logger)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _queueNotifier = queueNotifier;
        _logger = logger;
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

        // 2. Atomic MongoDB queue lock
        var apt = await _appointmentRepository.CallNextPatientAtomicAsync(request.DoctorId, request.RoomId, ct);
        if (apt == null)
        {
            _logger.LogInformation("Queue empty for room {RoomId}", request.RoomId);
            return null;
        }

        // 3. Resolve display details
        var doctor = await _appointmentRepository.GetDoctorAsync(request.DoctorId, ct);
        var doctorName = doctor?.FullName ?? "Bác sĩ phụ trách";

        var patient = await _patientRepository.GetByIdOrCodeAsync(apt.PatientId, ct);
        var patientName = patient?.FullName ?? apt.PatientId;
        var patientPhone = patient?.PhoneNumber;

        var dto = new QueueItemDto
        {
            AppointmentId = apt.Id,
            AppointmentCode = apt.AppointmentCode,
            QueueNumber = apt.QueueNumber,
            PatientId = apt.PatientId,
            PatientName = patientName,
            PatientPhone = patientPhone,
            Status = apt.Status.ToString(),
            RoomId = request.RoomId,
            DoctorId = request.DoctorId,
            DoctorName = doctorName,
            RecallCount = apt.RecallCount,
            ConsultationStartTime = apt.ConsultationStartTime,
            CreatedAt = apt.CreatedAt
        };

        // 4. SignalR Broadcast
        await _queueNotifier.NotifyPatientCalledAsync(request.RoomId, new
        {
            queueNumber = dto.QueueNumberFormatted,
            room = request.RoomId,
            name = dto.PatientName,
            appointmentId = dto.AppointmentId,
            doctorName = dto.DoctorName
        }, ct);

        await _queueNotifier.NotifyQueueUpdatedAsync(request.RoomId, new { roomId = request.RoomId }, ct);

        _logger.LogInformation("Patient {PatientName} ({QueueNumber}) called to {RoomId} by {DoctorId}",
            dto.PatientName, dto.QueueNumberFormatted, request.RoomId, request.DoctorId);

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

        var dto = new QueueItemDto
        {
            AppointmentId = apt.Id,
            AppointmentCode = apt.AppointmentCode,
            QueueNumber = apt.QueueNumber,
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

        var inProgress = appointments.FirstOrDefault(a => a.Status == AppointmentStatus.InProgress && (roomId == null || a.RoomId == roomId));
        QueueItemDto? currentServing = null;
        if (inProgress != null)
        {
            doctors.TryGetValue(inProgress.DoctorId ?? string.Empty, out var doc);
            patientDict.TryGetValue(inProgress.PatientId, out var p);
            currentServing = new QueueItemDto
            {
                AppointmentId = inProgress.Id,
                AppointmentCode = inProgress.AppointmentCode,
                QueueNumber = inProgress.QueueNumber,
                PatientId = inProgress.PatientId,
                PatientName = p?.FullName ?? inProgress.PatientId,
                PatientPhone = p?.PhoneNumber,
                Status = inProgress.Status.ToString(),
                RoomId = inProgress.RoomId,
                DoctorId = inProgress.DoctorId,
                DoctorName = doc?.FullName ?? "Bác sĩ phụ trách",
                RecallCount = inProgress.RecallCount,
                ConsultationStartTime = inProgress.ConsultationStartTime,
                CreatedAt = inProgress.CreatedAt
            };
        }

        var waiting = appointments.Where(a => a.Status == AppointmentStatus.CheckedIn).Select(a =>
        {
            doctors.TryGetValue(a.DoctorId ?? string.Empty, out var doc);
            patientDict.TryGetValue(a.PatientId, out var p);
            return new QueueItemDto
            {
                AppointmentId = a.Id,
                AppointmentCode = a.AppointmentCode,
                QueueNumber = a.QueueNumber,
                PatientId = a.PatientId,
                PatientName = p?.FullName ?? a.PatientId,
                PatientPhone = p?.PhoneNumber,
                Status = a.Status.ToString(),
                RoomId = a.RoomId,
                DoctorId = a.DoctorId,
                DoctorName = doc?.FullName,
                RecallCount = a.RecallCount,
                ConsultationStartTime = a.ConsultationStartTime,
                CreatedAt = a.CreatedAt
            };
        }).ToList();

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
