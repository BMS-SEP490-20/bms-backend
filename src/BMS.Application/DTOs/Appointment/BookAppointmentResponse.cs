using System;
using System.Collections.Generic;
using System.Text;

namespace BMS.Application.DTOs.Appointment
{
    public class BookAppointmentResponse
    {
        public string AppointmentId { get; set; } = string.Empty;
        public string AppointmentCode { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;
        public string? DoctorId { get; set; }
        public string DoctorName { get; set; } = string.Empty;
        public string Specialty { get; set; } = string.Empty;
        public DateTime AppointmentDate { get; set; }
        public string SlotTime { get; set; } = string.Empty;
        public string EndTime { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? PackageId { get; set; }
        public string Message { get; set; } = "Appointment booked successfully. Please arrive on time with your ID.";
    }

    public class AvailableSlotDto
    {
        public string SlotTime { get; set; } = string.Empty;
        public int RemainingCapacity { get; set; }
        public bool IsAvailable { get; set; }
    }

    public class AvailableSlotsResponse
    {
        public string DoctorId { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public List<AvailableSlotDto> Slots { get; set; } = new();
    }

    public class DoctorOptionDto
    {
        public string DoctorId { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string Specialty { get; set; } = string.Empty;
    }

    public class ApiErrorResponse
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, string[]>? Errors { get; set; }
    }

}
