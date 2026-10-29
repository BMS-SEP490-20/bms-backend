using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using BMS.Application.DTOs.PatientFeedback;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;

namespace BMS.Application.Services
{
    /// <summary>
    /// Service managing patient feedback, ratings, and clinic manager responses.
    /// Operates directly on real MongoDB collections without hardcoded mock data.
    /// </summary>
    public class PatientFeedbackService : IPatientFeedbackService
    {
        private readonly IPatientFeedbackRepository _repository;

        /// <summary>Creates the service over the feedback repository.</summary>
        public PatientFeedbackService(IPatientFeedbackRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Retrieves filtered patient feedbacks from MongoDB.
        /// </summary>
        public Task<IReadOnlyList<PatientFeedback>> GetAllAsync(int? rating, string? category, string? status, string? searchTerm, CancellationToken ct = default)
            => _repository.GetAllAsync(rating, category, status, searchTerm, ct);

        /// <summary>
        /// Retrieves feedbacks submitted by a specific patient ID.
        /// </summary>
        public Task<IReadOnlyList<PatientFeedback>> GetByPatientIdAsync(string patientId, CancellationToken ct = default)
            => _repository.GetByPatientIdAsync(patientId, ct);

        /// <summary>
        /// Retrieves a single feedback record by identifier.
        /// </summary>
        public Task<PatientFeedback?> GetByIdAsync(string id, CancellationToken ct = default) => _repository.GetByIdAsync(id, ct);

        /// <summary>
        /// Creates a new patient feedback record and saves it directly to MongoDB.
        /// </summary>
        public async Task<PatientFeedback> CreateAsync(CreatePatientFeedbackDto dto, CancellationToken ct = default)
        {
            // Business rule: clamp rating strictly between 1 and 5 stars.
            var feedback = new PatientFeedback
            {
                FeedbackId = $"FB-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}",
                PatientId = !string.IsNullOrWhiteSpace(dto.PatientId) ? dto.PatientId : "PAT-ANON",
                PatientName = dto.IsAnonymous ? "Bệnh nhân ẩn danh" : (!string.IsNullOrWhiteSpace(dto.PatientName) ? dto.PatientName : "Bệnh nhân"),
                PatientEmail = dto.PatientEmail ?? "",
                PatientPhone = dto.PatientPhone,
                AppointmentId = dto.AppointmentId,
                DoctorName = dto.DoctorName,
                ServiceCategory = !string.IsNullOrWhiteSpace(dto.ServiceCategory) ? dto.ServiceCategory : "Dịch vụ chung",
                Rating = Math.Clamp(dto.Rating, 1, 5),
                Title = dto.Title,
                Comment = dto.Comment,
                Status = "PENDING",
                IsAnonymous = dto.IsAnonymous,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _repository.AddAsync(feedback, ct);
            return feedback;
        }

        /// <summary>
        /// Updates an existing feedback record.
        /// </summary>
        public async Task<PatientFeedback?> UpdateAsync(string id, UpdatePatientFeedbackDto dto, CancellationToken ct = default)
        {
            var existing = await _repository.GetByIdAsync(id, ct);
            if (existing == null) return null;

            if (!string.IsNullOrWhiteSpace(dto.Title)) existing.Title = dto.Title;
            if (!string.IsNullOrWhiteSpace(dto.Comment)) existing.Comment = dto.Comment;
            if (dto.Rating.HasValue) existing.Rating = Math.Clamp(dto.Rating.Value, 1, 5);
            if (!string.IsNullOrWhiteSpace(dto.ServiceCategory)) existing.ServiceCategory = dto.ServiceCategory;
            if (!string.IsNullOrWhiteSpace(dto.Status)) existing.Status = dto.Status;
            if (dto.IsAnonymous.HasValue)
            {
                existing.IsAnonymous = dto.IsAnonymous.Value;
                if (dto.IsAnonymous.Value) existing.PatientName = "Bệnh nhân ẩn danh";
            }

            existing.UpdatedAt = DateTime.UtcNow;

            return await _repository.UpdateAsync(existing, ct) ? existing : null;
        }

        /// <summary>
        /// Records an official clinic manager response to a patient review.
        /// </summary>
        public async Task<PatientFeedback?> RespondAsync(string id, RespondFeedbackDto dto, CancellationToken ct = default)
        {
            var existing = await _repository.GetByIdAsync(id, ct);
            if (existing == null) return null;

            existing.ManagerResponse = dto.Response;
            existing.RespondedBy = dto.RespondedBy ?? "Quản lý phòng khám (Clinic Manager)";
            existing.RespondedAt = DateTime.UtcNow;
            existing.Status = dto.Status ?? "RESPONDED";
            existing.UpdatedAt = DateTime.UtcNow;

            return await _repository.UpdateAsync(existing, ct) ? existing : null;
        }

        /// <summary>
        /// Moderates and deletes a feedback entry (reserved exclusively for Administrator role).
        /// Replaces offensive/inappropriate content with 'Feedback đã bị admin xóa' and sets status to DELETED.
        /// </summary>
        public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
        {
            var existing = await _repository.GetByIdAsync(id, ct);
            if (existing == null) return false;

            // BUSINESS RULE: Admin moderation performs soft deletion.
            // Preserves record in MongoDB for auditing while hiding inappropriate content.
            existing.Status = "DELETED";
            existing.Title = "Feedback đã bị admin xóa";
            existing.Comment = "Feedback đã bị admin xóa";
            existing.UpdatedAt = DateTime.UtcNow;

            return await _repository.UpdateAsync(existing, ct);
        }

        /// <summary>
        /// Computes live statistics based strictly on active documents stored in MongoDB (excluding DELETED).
        /// </summary>
        public Task<PatientFeedbackStatistics> GetStatisticsAsync(CancellationToken ct = default)
            => _repository.GetStatisticsAsync(ct);

    }
}
