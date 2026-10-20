using BMS.Core.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Core.Interfaces;

/// <summary>
/// Data repository contract for appointment notification persistence (UC22).
/// </summary>
public interface IAppointmentNotificationRepository
{
    Task<List<AppointmentNotification>> GetByAppointmentIdAsync(string appointmentId, CancellationToken ct = default);
    Task<List<AppointmentNotification>> GetByPatientIdAsync(string patientId, CancellationToken ct = default);
    Task<AppointmentNotification?> GetByIdAsync(string id, CancellationToken ct = default);
    Task InsertAsync(AppointmentNotification notification, CancellationToken ct = default);
    Task<bool> MarkAsReadAsync(string notificationId, CancellationToken ct = default);
}
