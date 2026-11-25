using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BMS.Application.DTOs.PatientFeedback
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
        public string? ServicePackageId { get; set; }
        public string? ServicePackageCode { get; set; }
        public string? ServicePackageName { get; set; }
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

        public string? AppointmentId { get; set; }
        public string? ServicePackageId { get; set; }
        public string? ServicePackageCode { get; set; }
        public string? ServicePackageName { get; set; }
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

    /// <summary>
    /// Sanitized public review item displayed in service package detail modals (no PII).
    /// </summary>
    public sealed record PublicPackageFeedbackDto(
        string FeedbackId,
        string DisplayAuthorName,
        string? ServicePackageId,
        string? ServicePackageCode,
        string? ServicePackageName,
        string ServiceCategory,
        string? DoctorName,
        int Rating,
        string Title,
        string Comment,
        string? ManagerResponse,
        string? RespondedBy,
        DateTime? RespondedAt,
        DateTime CreatedAt);

    /// <summary>
    /// Aggregate rating summary for a service package.
    /// </summary>
    public sealed record PackageFeedbackSummaryDto(
        string Key,
        string? ServicePackageId,
        string? ServicePackageCode,
        string? ServicePackageName,
        double AverageRating,
        long TotalReviews,
        IReadOnlyDictionary<int, long> RatingDistribution);

    /// <summary>
    /// Response payload for public package detail reviews.
    /// </summary>
    public sealed record PackageFeedbackDetailResponseDto(
        PackageFeedbackSummaryDto Summary,
        IReadOnlyList<PublicPackageFeedbackDto> Reviews);

    /// <summary>
    /// Used appointment package item eligible for patient feedback submission.
    /// </summary>
    public sealed record EligibleFeedbackPackageDto(
        string AppointmentId,
        string AppointmentCode,
        DateTime AppointmentDate,
        string AppointmentStatus,
        string? ServicePackageId,
        string? ServicePackageCode,
        string? ServicePackageName,
        string? DoctorName,
        bool AlreadyReviewed,
        string? ExistingFeedbackId,
        int? ExistingRating,
        string? ExistingTitle);
}

