using System.Security.Cryptography;
using BMS.Application.DTOs.BloodSamples;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;

namespace BMS.Application.Services
{
    /// <summary>
    /// Print Barcodes (#58) and Validate Sample (#73) on the shared <c>blood_samples</c> collection.
    /// Status values are restricted to <see cref="SampleStatus"/> so this API cannot write states the
    /// lab queue / timeline do not understand. Every status change is recorded in <c>status_history</c>.
    /// </summary>
    public class BloodSampleService : IBloodSampleService
    {
        private const int MaxBarcodeAttempts = 5;
        private const string ValidatedStatus = "validated";
        private const string RejectedStatus = "rejected";
        private readonly IBloodSampleRepository _repo;

        /// <summary>Creates the service over the blood sample repository.</summary>
        public BloodSampleService(IBloodSampleRepository repo)
        {
            _repo = repo;
        }

        /// <inheritdoc />
        public async Task<(IReadOnlyList<BloodSampleReadDto> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
        {
            var (items, total) = await _repo.GetPagedAsync(page, pageSize, ct);
            return (items.Select(ToDto).ToList(), total);
        }

        /// <inheritdoc />
        public async Task<BloodSampleReadDto?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var x = await _repo.GetByIdAsync(id, ct);
            return x is null ? null : ToDto(x);
        }

        /// <inheritdoc />
        public async Task<BloodSampleReadDto> CreateAsync(BloodSampleCreateDto dto, CancellationToken ct = default)
        {
            var status = dto.Status is null ? SampleStatus.Collected : ParseStatusOrThrow(dto.Status);
            var barcode = string.IsNullOrWhiteSpace(dto.Barcode) ? await GenerateUniqueBarcodeAsync(ct) : dto.Barcode.Trim();
            if (!string.IsNullOrWhiteSpace(dto.Barcode) && await _repo.BarcodeExistsAsync(barcode, ct))
                throw new DomainValidationException("SAMPLE_BARCODE_DUPLICATE", "Mã vạch mẫu đã tồn tại.");

            var now = DateTime.UtcNow;
            var entity = new Sample
            {
                Barcode = barcode,
                TestOrderId = dto.AppointmentId,
                SampleType = dto.SampleType,
                CollectedAt = dto.CollectedAt ?? now,
                VolumeMl = dto.Volume.HasValue ? (double)dto.Volume.Value : 0,
                Status = status,
                StorageLocation = dto.StorageLocation,
                Notes = dto.Notes,
                AssignedTechnicianId = dto.LabUserId,
                CreatedAt = now,
                StatusHistory = [new SampleStatusHistoryEntry { ToStatus = status.ToString(), ChangedAt = now, ChangedBy = dto.LabUserId }]
            };

            await _repo.CreateAsync(entity, ct);
            return ToDto(entity);
        }

        /// <inheritdoc />
        public async Task<bool> UpdateAsync(string id, BloodSampleUpdateDto dto, CancellationToken ct = default)
        {
            var current = await _repo.GetByIdAsync(id, ct);
            if (current is null) return false;

            string? newStatus = dto.Status is null ? null : ParseStatusOrThrow(dto.Status).ToString();
            var history = newStatus is not null && newStatus != current.StatusString
                ? new SampleStatusHistoryEntry { FromStatus = current.StatusString, ToStatus = newStatus, ChangedAt = DateTime.UtcNow, ChangedBy = dto.LabUserId, Notes = dto.Notes }
                : null;

            var changes = new SampleFieldChanges(
                SampleType: dto.SampleType,
                CollectedAt: dto.CollectedAt,
                VolumeMl: dto.Volume.HasValue ? (double)dto.Volume.Value : null,
                Status: newStatus,
                StorageLocation: dto.StorageLocation,
                Notes: dto.Notes,
                AssignedTechnicianId: dto.LabUserId);
            return await _repo.UpdateFieldsAsync(current.Id, changes, history, ct);
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => _repo.SoftDeleteAsync(id, ct);

        /// <inheritdoc />
        public async Task<bool> ValidateSampleAsync(string id, string status, string notes, string labUserId, CancellationToken ct = default)
        {
            var current = await _repo.GetByIdAsync(id, ct);
            if (current is null) return false;

            var now = DateTime.UtcNow;
            var operatorId = string.IsNullOrWhiteSpace(labUserId) ? null : labUserId;
            // Validate Sample (#73) maps onto the ISO 15189 reception step: an accepted specimen is Received,
            // a rejected one is Rejected with the reason kept, instead of writing free-text statuses.
            SampleFieldChanges changes = status.Trim().ToLowerInvariant() switch
            {
                ValidatedStatus => new(Status: SampleStatus.Received.ToString(), Notes: notes, AssignedTechnicianId: operatorId,
                    ReceivedAt: now, ReceivedBy: operatorId),
                RejectedStatus => new(Status: SampleStatus.Rejected.ToString(), Notes: notes, AssignedTechnicianId: operatorId,
                    RejectionReason: notes, RejectedAt: now),
                _ => throw new DomainValidationException("INVALID_VALIDATION_STATUS", "Trạng thái kiểm định chỉ nhận 'validated' hoặc 'rejected'.")
            };
            var history = new SampleStatusHistoryEntry { FromStatus = current.StatusString, ToStatus = changes.Status!, ChangedAt = now, ChangedBy = operatorId, Notes = notes };
            return await _repo.UpdateFieldsAsync(current.Id, changes, history, ct);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<BloodSampleReadDto>> GetBloodSampleByAppointmentIdAsync(string appointmentId, CancellationToken ct = default)
        {
            var list = await _repo.GetByAppointmentIdAsync(appointmentId, ct);
            return list.Select(ToDto).ToList();
        }

        private static SampleStatus ParseStatusOrThrow(string raw) =>
            Enum.TryParse<SampleStatus>(raw.Trim(), true, out var s) && Enum.IsDefined(s)
                ? s
                : throw new DomainValidationException("INVALID_SAMPLE_STATUS", $"Trạng thái mẫu '{raw}' không hợp lệ.");

        // Barcode format follows the BMS convention SMP-YYYYMMDD-XXXX (same as LabQueueService).
        private async Task<string> GenerateUniqueBarcodeAsync(CancellationToken ct)
        {
            for (var i = 0; i < MaxBarcodeAttempts; i++)
            {
                var candidate = $"SMP-{DateTime.UtcNow:yyyyMMdd}-{RandomNumberGenerator.GetInt32(0, 10000):D4}";
                if (!await _repo.BarcodeExistsAsync(candidate, ct)) return candidate;
            }
            throw new DomainValidationException("SAMPLE_BARCODE_EXHAUSTED", "Không thể cấp mã vạch mẫu, vui lòng thử lại.");
        }

        private static BloodSampleReadDto ToDto(Sample x) => new()
        {
            SampleId = x.Id,
            Barcode = string.IsNullOrWhiteSpace(x.Barcode) ? x.Id : x.Barcode,
            AppointmentId = x.TestOrderId,
            SampleType = x.SampleType,
            CollectedAt = x.CollectedAt,
            Volume = (decimal)x.VolumeMl,
            Status = x.StatusString,
            StorageLocation = x.StorageLocation,
            Notes = x.Notes,
            LabUserId = x.AssignedTechnicianId
        };
    }
}
