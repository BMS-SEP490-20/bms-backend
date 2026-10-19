using BMS.Application.DTOs.Appointment;
using System;
using System.Collections.Generic;
using System.Text;

namespace BMS.Application.Interfaces
{
    public interface IAppointmentService
    {
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
    }
}
