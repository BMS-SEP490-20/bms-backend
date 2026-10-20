using BMS.Application.DTOs.Appointment;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Application.Interfaces
{
    public interface IAppointmentService
    {
        // UC21: Book Appointment & Slot Management
        Task<BookAppointmentResponse> BookAsync(
            BookAppointmentRequest request,
            string currentUserId,
            CancellationToken ct = default);

        Task<AvailableSlotsResponse> GetAvailableSlotsAsync(
            string doctorId,
            DateTime date,
            CancellationToken ct = default);

        Task<List<DoctorOptionDto>> GetAvailableDoctorsAsync(
            DateTime date,
            string? specialty = null,
            CancellationToken ct = default);

        Task<BookAppointmentResponse?> GetByIdAsync(string appointmentId, CancellationToken ct = default);

        // UC13: Manage Appointments (CRUD & Overview)
        Task<IEnumerable<AppointmentReadDto>> GetAllAsync(CancellationToken ct = default);
        Task<AppointmentReadDto?> GetDetailByIdAsync(string id, CancellationToken ct = default);
        Task<AppointmentReadDto> CreateAsync(AppointmentCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(string id, AppointmentUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(string id, CancellationToken ct = default);
        Task<IEnumerable<AppointmentReadDto>> GetByPatientIdAsync(string patientId, CancellationToken ct = default);
    }
}
