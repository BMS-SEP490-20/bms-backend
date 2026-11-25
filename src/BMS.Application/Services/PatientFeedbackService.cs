using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using BMS.Application.DTOs.Notifications;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;

namespace BMS.Application.Services
{
    /// <summary>
    /// Service managing patient feedback, ratings, and clinic manager responses.
    /// Operates directly on real MongoDB collections without hardcoded mock data.
    /// </summary>
    public class PatientFeedbackService : IPatientFeedbackService
    {
        private readonly IPatientFeedbackRepository _repository;
        private readonly IPatientRepository? _patients;
        private readonly IMongoDatabase? _database;

        /// <summary>Creates the service over the feedback repository.</summary>
        /// <summary>Creates the service over the feedback repository.</summary>
        public PatientFeedbackService(
            IPatientFeedbackRepository repository,
            IPatientRepository? patients = null,
            IMongoDatabase? database = null)
        {
            _repository = repository;
            _patients = patients;
            _database = database;
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
            var eligibility = await CheckEligibilityAsync(dto.PatientId ?? string.Empty, ct);
            if (!eligibility.IsEligible)
            {
                throw new DomainValidationException("FEEDBACK_NOT_ELIGIBLE", eligibility.Reason);
            }

            // Business rule: clamp rating strictly between 1 and 5 stars.
            var feedback = new PatientFeedback
            {
                FeedbackId = $"FB-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}",
                PatientId = !string.IsNullOrWhiteSpace(dto.PatientId) ? dto.PatientId : "PAT-ANON",
                PatientName = dto.IsAnonymous ? "Bệnh nhân ẩn danh" : (!string.IsNullOrWhiteSpace(dto.PatientName) ? dto.PatientName : "Bệnh nhân"),
                PatientEmail = dto.PatientEmail ?? "",
                PatientPhone = dto.PatientPhone,
                AppointmentId = !string.IsNullOrWhiteSpace(dto.AppointmentId) ? dto.AppointmentId : eligibility.CompletedReferenceCodes.FirstOrDefault(),
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
        /// <summary>
        /// Patient deletes their own feedback permanently.
        /// </summary>
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
            => _repository.DeleteAsync(id, ct);

        /// <summary>
        /// Computes live statistics based strictly on active documents stored in MongoDB (excluding DELETED / HIDDEN_BY_ADMIN).
        /// </summary>
        public Task<PatientFeedbackStatistics> GetStatisticsAsync(CancellationToken ct = default)
            => _repository.GetStatisticsAsync(ct);

        public async Task<FeedbackEligibilityDto> CheckEligibilityAsync(string accountOrPatientId, CancellationToken ct = default)
        {
            if (_database == null || string.IsNullOrWhiteSpace(accountOrPatientId))
            {
                return new FeedbackEligibilityDto
                {
                    IsEligible = true,
                    CompletedCount = 1,
                    Reason = "Đủ điều kiện đánh giá."
                };
            }

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { accountOrPatientId.Trim() };
            if (_patients != null)
            {
                var pat = await _patients.GetByIdOrCodeAsync(accountOrPatientId.Trim(), ct)
                       ?? await _patients.GetByAccountIdAsync(accountOrPatientId.Trim(), ct);
                if (pat != null)
                {
                    if (!string.IsNullOrWhiteSpace(pat.Id)) keys.Add(pat.Id);
                    if (!string.IsNullOrWhiteSpace(pat.PatientCode)) keys.Add(pat.PatientCode);
                    if (!string.IsNullOrWhiteSpace(pat.AccountId)) keys.Add(pat.AccountId);
                }
            }

            // Collect refunded appointment/order IDs so refunded orders do NOT count as completed successfully
            var refundedRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var refundsCol = _database.GetCollection<BsonDocument>("refunds");
                var rf = Builders<BsonDocument>.Filter;
                var refundDocs = await refundsCol.Find(
                    rf.Ne("status", "Rejected") & rf.Ne("Status", "Rejected")
                ).ToListAsync(ct);
                foreach (var r in refundDocs)
                {
                    foreach (var field in new[] { "appointment_id", "appointmentId", "order_id", "orderId", "invoice_id", "invoiceId" })
                    {
                        if (r.TryGetValue(field, out var val) && val.IsString && !string.IsNullOrWhiteSpace(val.AsString))
                            refundedRefs.Add(val.AsString);
                    }
                }
            }
            catch { /* non-blocking */ }

            var completedCodes = new List<string>();
            try
            {
                var apptsCol = _database.GetCollection<BsonDocument>("appointments");
                var af = Builders<BsonDocument>.Filter;
                var apptDocs = await apptsCol.Find(
                    af.Or(af.In("patient_id", keys), af.In("patientId", keys), af.In("PatientId", keys))
                ).ToListAsync(ct);

                foreach (var a in apptDocs)
                {
                    var st = a.TryGetValue("status", out var s1) ? s1.ToString() : (a.TryGetValue("Status", out var s2) ? s2.ToString() : "");
                    if (!string.Equals(st, "Completed", StringComparison.OrdinalIgnoreCase) && st != "4") continue;

                    var id = a.TryGetValue("_id", out var idv) ? idv.ToString() : "";
                    var code = a.TryGetValue("appointment_code", out var cv) && cv.IsString ? cv.AsString : id;
                    if (!refundedRefs.Contains(id ?? "") && !refundedRefs.Contains(code ?? ""))
                    {
                        if (!string.IsNullOrWhiteSpace(code)) completedCodes.Add(code);
                    }
                }
            }
            catch { /* non-blocking */ }

            try
            {
                var ordersCol = _database.GetCollection<BsonDocument>("test_orders");
                var of = Builders<BsonDocument>.Filter;
                var orderDocs = await ordersCol.Find(
                    of.Or(of.In("patient_id", keys), of.In("patientId", keys), of.In("PatientId", keys))
                ).ToListAsync(ct);

                foreach (var o in orderDocs)
                {
                    var st = o.TryGetValue("status", out var s1) ? s1.ToString() : (o.TryGetValue("Status", out var s2) ? s2.ToString() : "");
                    if (!string.Equals(st, "Completed", StringComparison.OrdinalIgnoreCase)) continue;

                    var id = o.TryGetValue("_id", out var idv) ? idv.ToString() : "";
                    var num = o.TryGetValue("order_number", out var nv) && nv.IsString ? nv.AsString : id;
                    if (!refundedRefs.Contains(id ?? "") && !refundedRefs.Contains(num ?? ""))
                    {
                        if (!string.IsNullOrWhiteSpace(num) && !completedCodes.Contains(num)) completedCodes.Add(num);
                    }
                }
            }
            catch { /* non-blocking */ }

            if (completedCodes.Count == 0)
            {
                return new FeedbackEligibilityDto
                {
                    IsEligible = false,
                    CompletedCount = 0,
                    CompletedReferenceCodes = [],
                    Reason = "Bạn cần hoàn thành ít nhất 1 lịch khám / đơn xét nghiệm thành công (không hoàn tiền, không hủy/lỗi) trước khi gửi đóng góp ý kiến."
                };
            }

            return new FeedbackEligibilityDto
            {
                IsEligible = true,
                CompletedCount = completedCodes.Count,
                CompletedReferenceCodes = completedCodes,
                Reason = $"Bạn có {completedCodes.Count} đơn đã hoàn thành thành công."
            };
        }

        public async Task<PatientFeedback?> RequestAdminDeleteAsync(string id, string adminName, string? reason, CancellationToken ct = default)
        {
            var existing = await _repository.GetByIdAsync(id, ct);
            if (existing == null || existing.PatientDeleted) return null;

            existing.AdminDeleteRequested = true;
            existing.AdminDeleteReason = string.IsNullOrWhiteSpace(reason) ? "Nội dung cần rà soát lại" : reason.Trim();
            existing.AdminDeleteRequestedBy = string.IsNullOrWhiteSpace(adminName) ? "Quản trị viên (Admin)" : adminName.Trim();
            existing.AdminDeleteRequestedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;

            var ok = await _repository.UpdateAsync(existing, ct);
            if (!ok) return null;

            if (_notifications != null && !string.IsNullOrWhiteSpace(existing.PatientId))
            {
                try
                {
                    await _notifications.CreateNotificationAsync(new CreateNotificationDto
                    {
                        UserId = existing.PatientId,
                        Role = RoleNames.Patient,
                        Title = "Admin yêu cầu ẩn/xóa góp ý của bạn",
                        Message = $"Quản trị viên ({existing.AdminDeleteRequestedBy}) đề nghị ẩn góp ý '{existing.Title}' (Lý do: {existing.AdminDeleteReason}). Vui lòng vào mục Đóng góp ý kiến để xác nhận đồng ý hoặc từ chối.",
                        Type = "FeedbackDeleteRequest",
                        ReferenceId = existing.FeedbackId
                    }, ct);
                }
                catch { /* non-blocking */ }
            }

            return existing;
        }

        public async Task<PatientFeedback?> RespondAdminDeleteConsentAsync(string id, bool approved, CancellationToken ct = default)
        {
            var existing = await _repository.GetByIdAsync(id, ct);
            if (existing == null || existing.PatientDeleted) return null;

            if (approved)
            {
                existing.PatientApprovedDelete = true;
                existing.HiddenByAdminWithConsent = true;
                existing.AdminDeleteRequested = false;
                existing.Status = "HIDDEN_BY_ADMIN";
                existing.Title = "feedback được ẩn bởi admin theo sự đồng ý của patient";
                existing.Comment = "feedback được ẩn bởi admin theo sự đồng ý của patient";
            }
            else
            {
                existing.AdminDeleteRequested = false;
                existing.PatientApprovedDelete = false;
            }
            existing.UpdatedAt = DateTime.UtcNow;

            var ok = await _repository.UpdateAsync(existing, ct);
            if (!ok) return null;

            if (_notifications != null)
            {
                try
                {
                    await _notifications.CreateNotificationAsync(new CreateNotificationDto
                    {
                        UserId = "ROLE_ADMIN",
                        Role = RoleNames.Admin,
                        Title = approved ? "Bệnh nhân đã đồng ý ẩn góp ý" : "Bệnh nhân từ chối ẩn góp ý",
                        Message = approved
                            ? $"Bệnh nhân đã đồng ý ẩn góp ý #{existing.FeedbackId}. Góp ý đã chuyển sang trạng thái ẩn."
                            : $"Bệnh nhân không đồng ý ẩn góp ý #{existing.FeedbackId}.",
                        Type = "FeedbackDeleteConsent",
                        ReferenceId = existing.FeedbackId
                    }, ct);
                }
                catch { /* non-blocking */ }
            }

            return existing;
        }
    }
}
