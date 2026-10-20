using BMS.Core.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Core.Interfaces
{
    public interface IAppointmentRepository
    {
        Task<List<Appointment>> GetAllAsync(CancellationToken ct = default);
        Task<Appointment?> GetByIdAsync(string id, CancellationToken ct = default);
        Task<Appointment?> GetByCodeAsync(string appointmentCode, CancellationToken ct = default);
        Task<List<Appointment>> GetByPatientIdAsync(string patientId, CancellationToken ct = default);
        Task<List<Appointment>> GetByDoctorAndDateAsync(string doctorId, DateTime date, CancellationToken ct = default);
        Task<int> CountByDoctorDateStartAsync(string doctorId, DateTime date, TimeSpan startTime, CancellationToken ct = default);
        Task<int> CountByDoctorAndDateAsync(string doctorId, DateTime date, CancellationToken ct = default);
        Task<string> GenerateNextCodeAsync(DateTime date, CancellationToken ct = default);
        Task InsertAsync(Appointment appointment, CancellationToken ct = default);
        Task UpdateAsync(Appointment appointment, CancellationToken ct = default);
        Task UpdateStatusAsync(string id, AppointmentStatus newStatus, CancellationToken ct = default);
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);

        Task<List<DoctorShift>> GetDoctorShiftsAsync(string doctorId, DateTime date, CancellationToken ct = default);
        Task<List<DoctorShift>> GetShiftsByDateAsync(DateTime date, CancellationToken ct = default);
        Task<Doctor?> GetDoctorAsync(string doctorId, CancellationToken ct = default);
        Task<List<Doctor>> GetDoctorsByIdsAsync(IEnumerable<string> doctorIds, CancellationToken ct = default);
    }
}
