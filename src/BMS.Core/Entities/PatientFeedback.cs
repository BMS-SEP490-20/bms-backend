using System;
using System.ComponentModel.DataAnnotations;

namespace Testorder_service.Models.Entities
{
    /// <summary>
    /// Represents patient feedback, ratings, and reviews regarding laboratory and clinic services.
    /// Patients submit feedback, and Clinic Managers review and reply.
    /// Doctors, Receptionists, and Admins can also inspect feedbacks.
    /// </summary>
    public class PatientFeedback
    {
        [Key]
        public string FeedbackId { get; set; } = null!;

        public string PatientId { get; set; } = null!;

        public string PatientName { get; set; } = null!;

        public string PatientEmail { get; set; } = null!;

        public string? PatientPhone { get; set; }

        public string? AppointmentId { get; set; }

        public string? DoctorName { get; set; }

        public string ServiceCategory { get; set; } = "Dịch vụ chung"; // Đón tiếp & Lễ tân, Lấy mẫu máu & Xét nghiệm, Tư vấn bác sĩ, v.v.

        [Range(1, 5)]
        public int Rating { get; set; } = 5;

        public string Title { get; set; } = null!;

        public string Comment { get; set; } = null!;

        public string Status { get; set; } = "PENDING"; // PENDING, REVIEWED, RESPONDED, RESOLVED, HIDDEN

        public string? ManagerResponse { get; set; }

        public string? RespondedBy { get; set; }

        public DateTime? RespondedAt { get; set; }

        public bool IsAnonymous { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
