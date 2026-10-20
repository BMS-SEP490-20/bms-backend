using System;

namespace BMS.Application.DTOs.Appointment
{
    public class AppointmentReadDto
    {
        public string AppointmentId { get; set; } = string.Empty;
        public string? AppointmentCode { get; set; }
        public string? TestPackageId { get; set; }
        public string? PackageId { get; set; }
        public string? TestPackageName { get; set; }
        public string? DoctorShiftId { get; set; }
        public string? DoctorId { get; set; }
        public string? DoctorName { get; set; }
        public string? Specialty { get; set; }
        public string? Status { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public string? PatientId { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? LabAssignmentId { get; set; }
        public DateTime? AppointmentTime { get; set; }
        public DateTime? AppointmentDate { get; set; }
        public string? SlotTime { get; set; }
        public string? EndTime { get; set; }
        public string? AppointmentType { get; set; }
        public string? ReasonForRescheduling { get; set; }
        public string? Notes { get; set; }
        public string? GuestName { get; set; }
        public string? GuestPhone { get; set; }
        public string? CollectionAddress { get; set; }
        public int? QueueNumber { get; set; }
    }
}
