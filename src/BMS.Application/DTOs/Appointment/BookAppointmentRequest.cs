using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BMS.Application.DTOs.Appointment
{
    /// <summary>Request body for POST /api/appointments (UC21).</summary>
    public class BookAppointmentRequest
    {
        [Required(ErrorMessage = "PatientId is required.")]
        public string PatientId { get; set; } = string.Empty;

        [Required(ErrorMessage = "DoctorId is required.")]
        public string DoctorId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Appointment date is required.")]
        public DateTime AppointmentDate { get; set; }

        /// <summary>Slot start as HH:mm (mapped to Appointment.StartTime).</summary>
        [Required(ErrorMessage = "Slot time is required.")]
        [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "SlotTime must be HH:mm format.")]
        public string SlotTime { get; set; } = string.Empty;

        [Required(ErrorMessage = "At least one package must be selected.")]
        [MinLength(1, ErrorMessage = "At least one package must be selected.")]
        public List<string> PackageIds { get; set; } = new();

        [MaxLength(500)]
        public string? Notes { get; set; }

        /// <summary>ClinicVisit (default) or HomePhlebotomy.</summary>
        public string Type { get; set; } = "ClinicVisit";

        public string? CollectionAddress { get; set; }
    }
}
