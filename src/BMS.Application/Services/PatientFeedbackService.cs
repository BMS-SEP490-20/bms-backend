using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;
using Testorder_service.Repositories.Interface;
using Testorder_service.Services.Interface;

namespace Testorder_service.Services.Implement
{
    /// <summary>
    /// Service managing patient feedback, ratings, and clinic manager responses.
    /// Operates directly on real MongoDB collections without hardcoded mock data.
    /// </summary>
    public class PatientFeedbackService : IPatientFeedbackService
    {
        private readonly IPatientFeedbackRepository _repository;

        public PatientFeedbackService(IPatientFeedbackRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Retrieves filtered patient feedbacks from MongoDB.
        /// </summary>
        public async Task<IEnumerable<PatientFeedback>> GetAllAsync(int? rating, string? category, string? status, string? searchTerm)
        {
            return await _repository.GetAllAsync(rating, category, status, searchTerm);
        }

        /// <summary>
        /// Retrieves feedbacks submitted by a specific patient ID.
        /// </summary>
        public async Task<IEnumerable<PatientFeedback>> GetByPatientIdAsync(string patientId)
        {
            return await _repository.GetByPatientIdAsync(patientId);
        }

        /// <summary>
        /// Retrieves a single feedback record by identifier.
        /// </summary>
        public async Task<PatientFeedback?> GetByIdAsync(string id)
        {
            return await _repository.GetByIdAsync(id);
        }

        /// <summary>
        /// Creates a new patient feedback record and saves it directly to MongoDB.
        /// </summary>
        public async Task<PatientFeedback> CreateAsync(CreatePatientFeedbackDto dto)
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

            await _repository.AddAsync(feedback);
            await _repository.SaveChangesAsync();
            return feedback;
        }

        /// <summary>
        /// Updates an existing feedback record.
        /// </summary>
        public async Task<PatientFeedback?> UpdateAsync(string id, UpdatePatientFeedbackDto dto)
        {
            var existing = await _repository.GetByIdAsync(id);
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

            await _repository.UpdateAsync(existing);
            await _repository.SaveChangesAsync();
            return existing;
        }

        /// <summary>
        /// Records an official clinic manager response to a patient review.
        /// </summary>
        public async Task<PatientFeedback?> RespondAsync(string id, RespondFeedbackDto dto)
        {
            var existing = await _repository.GetByIdAsync(id);
            if (existing == null) return null;

            existing.ManagerResponse = dto.Response;
            existing.RespondedBy = dto.RespondedBy ?? "Quản lý phòng khám (Clinic Manager)";
            existing.RespondedAt = DateTime.UtcNow;
            existing.Status = dto.Status ?? "RESPONDED";
            existing.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateAsync(existing);
            await _repository.SaveChangesAsync();
            return existing;
        }

        /// <summary>
        /// Moderates and deletes a feedback entry (reserved exclusively for Administrator role).
        /// Replaces offensive/inappropriate content with 'Feedback đã bị admin xóa' and sets status to DELETED.
        /// </summary>
        public async Task<bool> DeleteAsync(string id)
        {
            var existing = await _repository.GetByIdAsync(id);
            if (existing == null) return false;

            // BUSINESS RULE: Admin moderation performs soft deletion.
            // Preserves record in MongoDB for auditing while hiding inappropriate content.
            existing.Status = "DELETED";
            existing.Title = "Feedback đã bị admin xóa";
            existing.Comment = "Feedback đã bị admin xóa";
            existing.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateAsync(existing);
            await _repository.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Computes live statistics based strictly on active documents stored in MongoDB (excluding DELETED).
        /// </summary>
        public async Task<object> GetStatisticsAsync()
        {
            var all = (await _repository.GetAllAsync(null, null, null, null)).ToList();
            var active = all.Where(f => f.Status != "DELETED").ToList();

            var total = active.Count;
            var averageRating = total > 0 ? Math.Round(active.Average(f => f.Rating), 1) : 5.0;
            var respondedCount = active.Count(f => !string.IsNullOrEmpty(f.ManagerResponse));
            var pendingCount = active.Count(f => f.Status == "PENDING");

            var ratingCounts = new Dictionary<int, int>
            {
                { 5, active.Count(f => f.Rating == 5) },
                { 4, active.Count(f => f.Rating == 4) },
                { 3, active.Count(f => f.Rating == 3) },
                { 2, active.Count(f => f.Rating == 2) },
                { 1, active.Count(f => f.Rating == 1) },
            };

            return new
            {
                totalFeedbacks = total,
                averageRating,
                respondedFeedbacks = respondedCount,
                pendingFeedbacks = pendingCount,
                ratingDistribution = ratingCounts
            };
        }

    }
}
