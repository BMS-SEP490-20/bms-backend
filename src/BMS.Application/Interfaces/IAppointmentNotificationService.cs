using BMS.Application.DTOs.Appointment;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract for appointment notification & confirmation management (UC22).
/// </summary>
public interface IAppointmentNotificationService
{
    Task<AppointmentConfirmationDto?> GetConfirmationAsync(string appointmentIdOrCode, CancellationToken ct = default);
    Task<bool> SendNotificationAsync(string appointmentId, SendNotificationRequestDto request, CancellationToken ct = default);
    Task<List<AppointmentNotificationDto>> GetPatientNotificationsAsync(string patientId, CancellationToken ct = default);
    Task<bool> MarkAsReadAsync(string notificationId, CancellationToken ct = default);
}
