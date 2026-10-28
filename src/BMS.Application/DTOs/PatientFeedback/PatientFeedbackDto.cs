using System;
using System.ComponentModel.DataAnnotations;

namespace Testorder_service.Models.DTO
{
    /// <summary>
    /// Data transfer object for patient feedback submission.
    /// </summary>
    public class CreatePatientFeedbackDto
    {
        public string? PatientId { get; set; }
        public string? PatientName { get; set; }
        public string? PatientEmail { get; set; }
        public string? PatientPhone { get; set; }
        public string? AppointmentId { get; set; }
        public string? DoctorName { get; set; }
        public string ServiceCategory { get; set; } = "Dịch vụ chung";

        [Range(1, 5)]
        public int Rating { get; set; } = 5;

        [Required]
        public string Title { get; set; } = null!;

        [Required]
        public string Comment { get; set; } = null!;

        public bool IsAnonymous { get; set; } = false;
    }

    /// <summary>
    /// Data transfer object for updating an existing feedback record.
    /// </summary>
    public class UpdatePatientFeedbackDto
    {
        public string? Title { get; set; }
        public string? Comment { get; set; }

        [Range(1, 5)]
        public int? Rating { get; set; }

        public string? ServiceCategory { get; set; }
        public string? Status { get; set; }
        public bool? IsAnonymous { get; set; }
    }

    /// <summary>
    /// Data transfer object for clinic manager response to patient feedback.
    /// </summary>
    public class RespondFeedbackDto
    {
        [Required]
        public string Response { get; set; } = null!;

        public string? RespondedBy { get; set; }
        public string? Status { get; set; } = "RESPONDED";
    }
}

