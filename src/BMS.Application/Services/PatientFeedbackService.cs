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
        private readonly IAppointmentRepository? _appointmentRepository;
        private readonly ITestPackageRepository? _packageRepository;

        /// <summary>Creates the service over the feedback repository and optional appointment/package lookups.</summary>
        public PatientFeedbackService(
            IPatientFeedbackRepository repository,
            IAppointmentRepository? appointmentRepository = null,
            ITestPackageRepository? packageRepository = null)
        {
            _repository = repository;
            _appointmentRepository = appointmentRepository;
            _packageRepository = packageRepository;
        }

        /// <summary>
        /// Retrieves filtered patient feedbacks from MongoDB.
        /// </summary>
        public Task<IReadOnlyList<PatientFeedback>> GetAllAsync(int? rating, string? category, string? status, string? searchTerm, string? servicePackageId = null, CancellationToken ct = default)
            => _repository.GetAllAsync(rating, category, status, searchTerm, servicePackageId, ct);

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
        /// Resolves linked appointment and canonical test package metadata when provided.
        /// </summary>
        public async Task<PatientFeedback> CreateAsync(CreatePatientFeedbackDto dto, CancellationToken ct = default)
        {
            var packageId = dto.ServicePackageId?.Trim();
            var packageCode = dto.ServicePackageCode?.Trim();
            var packageName = dto.ServicePackageName?.Trim();
            var doctorName = dto.DoctorName?.Trim();
            var appointmentCodeOrId = dto.AppointmentId?.Trim();

            if (!string.IsNullOrWhiteSpace(appointmentCodeOrId) && _appointmentRepository != null)
            {
                var apt = await _appointmentRepository.GetByIdAsync(appointmentCodeOrId, ct)
                          ?? await _appointmentRepository.GetByCodeAsync(appointmentCodeOrId, ct);
                if (apt != null && (string.IsNullOrWhiteSpace(dto.PatientId) || string.Equals(apt.PatientId, dto.PatientId, StringComparison.OrdinalIgnoreCase)))
                {
                    appointmentCodeOrId = !string.IsNullOrWhiteSpace(apt.AppointmentCode) ? apt.AppointmentCode : apt.Id;
                    if (string.IsNullOrWhiteSpace(packageId)) packageId = apt.PackageId;
                    if (string.IsNullOrWhiteSpace(packageCode)) packageCode = apt.PackageCode;
                    if (string.IsNullOrWhiteSpace(doctorName) && !string.IsNullOrWhiteSpace(apt.DoctorId))
                    {
                        var doc = await _appointmentRepository.GetDoctorAsync(apt.DoctorId, ct);
                        if (doc != null && !string.IsNullOrWhiteSpace(doc.FullName))
                            doctorName = doc.FullName;
                    }
                }
            }

            if (_packageRepository != null && (!string.IsNullOrWhiteSpace(packageId) || !string.IsNullOrWhiteSpace(packageCode)))
            {
                var lookup = !string.IsNullOrWhiteSpace(packageId) ? packageId : packageCode!;
                var pkg = await _packageRepository.GetByIdOrCodeAsync(lookup, includeInactive: true, ct)
                          ?? (!string.IsNullOrWhiteSpace(packageCode) && packageCode != lookup
                              ? await _packageRepository.GetByIdOrCodeAsync(packageCode, includeInactive: true, ct)
                              : null);
                if (pkg != null)
                {
                    packageId = pkg.Id;
                    packageCode = pkg.PackageCode;
                    packageName = pkg.PackageName;
                }
            }

            // Business rule: clamp rating strictly between 1 and 5 stars.
            var feedback = new PatientFeedback
            {
                FeedbackId = $"FB-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}",
                PatientId = !string.IsNullOrWhiteSpace(dto.PatientId) ? dto.PatientId : "PAT-ANON",
                PatientName = dto.IsAnonymous ? "Bệnh nhân ẩn danh" : (!string.IsNullOrWhiteSpace(dto.PatientName) ? dto.PatientName : "Bệnh nhân"),
                PatientEmail = dto.PatientEmail ?? "",
                PatientPhone = dto.PatientPhone,
                AppointmentId = appointmentCodeOrId,
                DoctorName = doctorName,
                ServicePackageId = packageId,
                ServicePackageCode = packageCode,
                ServicePackageName = packageName,
                ServiceCategory = !string.IsNullOrWhiteSpace(dto.ServiceCategory)
                    ? dto.ServiceCategory
                    : (!string.IsNullOrWhiteSpace(packageName) ? "Đánh giá gói xét nghiệm" : "Dịch vụ chung"),
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
            if (dto.AppointmentId != null) existing.AppointmentId = dto.AppointmentId;
            if (!string.IsNullOrWhiteSpace(dto.ServicePackageId) || !string.IsNullOrWhiteSpace(dto.ServicePackageCode))
            {
                existing.ServicePackageId = dto.ServicePackageId ?? existing.ServicePackageId;
                existing.ServicePackageCode = dto.ServicePackageCode ?? existing.ServicePackageCode;
                existing.ServicePackageName = dto.ServicePackageName ?? existing.ServicePackageName;
            }
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

        /// <inheritdoc />
        public async Task<IReadOnlyList<PackageFeedbackSummaryDto>> GetPackageRatingSummariesAsync(CancellationToken ct = default)
        {
            var active = await _repository.GetActiveWithPackageAsync(ct);
            if (active.Count == 0) return Array.Empty<PackageFeedbackSummaryDto>();

            var pkgCache = new Dictionary<string, TestPackage?>(StringComparer.OrdinalIgnoreCase);
            var groups = new Dictionary<string, (string? Id, string? Code, string? Name, List<PatientFeedback> Items)>(StringComparer.OrdinalIgnoreCase);

            foreach (var fb in active)
            {
                var rawKey = !string.IsNullOrWhiteSpace(fb.ServicePackageId) ? fb.ServicePackageId : fb.ServicePackageCode;
                if (string.IsNullOrWhiteSpace(rawKey)) continue;

                TestPackage? resolvedPkg = null;
                if (_packageRepository != null)
                {
                    if (!pkgCache.TryGetValue(rawKey, out resolvedPkg))
                    {
                        resolvedPkg = await _packageRepository.GetByIdOrCodeAsync(rawKey, includeInactive: true, ct);
                        if (resolvedPkg == null && !string.IsNullOrWhiteSpace(fb.ServicePackageCode) && !string.Equals(fb.ServicePackageCode, rawKey, StringComparison.OrdinalIgnoreCase))
                        {
                            resolvedPkg = await _packageRepository.GetByIdOrCodeAsync(fb.ServicePackageCode, includeInactive: true, ct);
                        }
                        pkgCache[rawKey] = resolvedPkg;
                        if (resolvedPkg != null)
                        {
                            if (!string.IsNullOrWhiteSpace(resolvedPkg.Id)) pkgCache[resolvedPkg.Id] = resolvedPkg;
                            if (!string.IsNullOrWhiteSpace(resolvedPkg.PackageCode)) pkgCache[resolvedPkg.PackageCode] = resolvedPkg;
                        }
                    }
                }

                var canonicalKey = resolvedPkg?.Id ?? fb.ServicePackageId ?? fb.ServicePackageCode ?? rawKey;
                if (!groups.TryGetValue(canonicalKey, out var entry))
                {
                    entry = (
                        resolvedPkg?.Id ?? fb.ServicePackageId,
                        resolvedPkg?.PackageCode ?? fb.ServicePackageCode,
                        resolvedPkg?.PackageName ?? fb.ServicePackageName,
                        new List<PatientFeedback>());
                    groups[canonicalKey] = entry;
                }
                entry.Items.Add(fb);
            }

            return groups.Select(kvp =>
            {
                var (pkgId, pkgCode, pkgName, items) = kvp.Value;
                var avg = items.Count > 0 ? Math.Round(items.Average(i => (double)i.Rating), 1) : 0.0;
                var dist = Enumerable.Range(1, 5).Reverse().ToDictionary(s => s, s => items.LongCount(i => i.Rating == s));
                return new PackageFeedbackSummaryDto(kvp.Key, pkgId, pkgCode, pkgName, avg, items.Count, dist);
            }).ToList();
        }

        /// <inheritdoc />
        public async Task<PackageFeedbackDetailResponseDto> GetPublicPackageFeedbackAsync(string packageIdOrCode, CancellationToken ct = default)
        {
            TestPackage? pkg = null;
            if (_packageRepository != null && !string.IsNullOrWhiteSpace(packageIdOrCode))
            {
                pkg = await _packageRepository.GetByIdOrCodeAsync(packageIdOrCode, includeInactive: true, ct);
            }

            var reviewsRaw = await _repository.GetByPackageAsync(pkg?.Id ?? packageIdOrCode, pkg?.PackageCode ?? packageIdOrCode, ct);
            var reviews = reviewsRaw.Select(f => new PublicPackageFeedbackDto(
                f.FeedbackId,
                f.IsAnonymous || string.IsNullOrWhiteSpace(f.PatientName) ? "Bệnh nhân ẩn danh" : f.PatientName,
                f.ServicePackageId ?? pkg?.Id,
                f.ServicePackageCode ?? pkg?.PackageCode,
                f.ServicePackageName ?? pkg?.PackageName,
                f.ServiceCategory,
                f.DoctorName,
                f.Rating,
                f.Title,
                f.Comment,
                f.ManagerResponse,
                f.RespondedBy,
                f.RespondedAt,
                f.CreatedAt)).ToList();

            var avg = reviews.Count > 0 ? Math.Round(reviews.Average(r => (double)r.Rating), 1) : 0.0;
            var dist = Enumerable.Range(1, 5).Reverse().ToDictionary(s => s, s => reviews.LongCount(r => r.Rating == s));
            var summary = new PackageFeedbackSummaryDto(
                pkg?.Id ?? packageIdOrCode,
                pkg?.Id,
                pkg?.PackageCode,
                pkg?.PackageName,
                avg,
                reviews.Count,
                dist);

            return new PackageFeedbackDetailResponseDto(summary, reviews);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<EligibleFeedbackPackageDto>> GetEligiblePackagesForPatientAsync(string patientId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(patientId) || _appointmentRepository == null)
                return Array.Empty<EligibleFeedbackPackageDto>();

            var appointments = await _appointmentRepository.GetByPatientIdAsync(patientId, ct);
            var usedAppointments = appointments
                .Where(a => a.Status is AppointmentStatus.Completed or AppointmentStatus.InProgress or AppointmentStatus.CheckedIn)
                .OrderByDescending(a => a.AppointmentDate)
                .ThenByDescending(a => a.CreatedAt)
                .ToList();

            if (usedAppointments.Count == 0)
                return Array.Empty<EligibleFeedbackPackageDto>();

            var existingFeedbacks = (await _repository.GetByPatientIdAsync(patientId, ct))
                .Where(f => !string.Equals(f.Status, "DELETED", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var doctorIds = usedAppointments.Where(a => !string.IsNullOrWhiteSpace(a.DoctorId)).Select(a => a.DoctorId!).Distinct().ToList();
            var doctorsMap = doctorIds.Count > 0
                ? (await _appointmentRepository.GetDoctorsByIdsAsync(doctorIds, ct)).ToDictionary(d => d.Id, d => d.FullName)
                : new Dictionary<string, string>();

            var pkgCache = new Dictionary<string, TestPackage?>(StringComparer.OrdinalIgnoreCase);
            var result = new List<EligibleFeedbackPackageDto>();

            foreach (var apt in usedAppointments)
            {
                var rawPkgKey = !string.IsNullOrWhiteSpace(apt.PackageId) ? apt.PackageId : apt.PackageCode;
                if (string.IsNullOrWhiteSpace(rawPkgKey)) continue;

                TestPackage? pkg = null;
                if (_packageRepository != null && !pkgCache.TryGetValue(rawPkgKey, out pkg))
                {
                    pkg = await _packageRepository.GetByIdOrCodeAsync(rawPkgKey, includeInactive: true, ct)
                          ?? (!string.IsNullOrWhiteSpace(apt.PackageCode) && apt.PackageCode != rawPkgKey
                              ? await _packageRepository.GetByIdOrCodeAsync(apt.PackageCode, includeInactive: true, ct)
                              : null);
                    pkgCache[rawPkgKey] = pkg;
                }

                var matchedFb = existingFeedbacks.FirstOrDefault(f =>
                    string.Equals(f.AppointmentId, apt.AppointmentCode, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(f.AppointmentId, apt.Id, StringComparison.OrdinalIgnoreCase));

                string? docName = !string.IsNullOrWhiteSpace(apt.DoctorId) && doctorsMap.TryGetValue(apt.DoctorId, out var dn) ? dn : null;

                result.Add(new EligibleFeedbackPackageDto(
                    apt.Id,
                    !string.IsNullOrWhiteSpace(apt.AppointmentCode) ? apt.AppointmentCode : apt.Id,
                    apt.AppointmentDate,
                    apt.Status.ToString(),
                    pkg?.Id ?? apt.PackageId,
                    pkg?.PackageCode ?? apt.PackageCode,
                    pkg?.PackageName ?? (!string.IsNullOrWhiteSpace(apt.PackageCode) ? $"Gói {apt.PackageCode}" : "Gói xét nghiệm lâm sàng"),
                    docName,
                    matchedFb != null,
                    matchedFb?.FeedbackId,
                    matchedFb?.Rating,
                    matchedFb?.Title));
            }

            return result;
        }

    }
}
