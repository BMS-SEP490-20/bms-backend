using BMS.Application.DTOs.TestResults;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services
{
    /// <summary>
    /// Test result CRUD and approval (I3-004) on <c>test_results</c>.
    /// Ported from the legacy Testorder microservice: EF Core (TestOrderManagementDbContext), the
    /// separate TestParameter table and the IAM gRPC client do not exist in BMS. Parameters are stored as
    /// <see cref="TestResultItem"/>s inside the result document, appointment data comes from
    /// <see cref="IAppointmentRepository"/>, and the "waiting review" email became an in-app notification.
    /// </summary>
    public class TestResultService : ITestResultService
    {
        private const string NotFoundCode = "TEST_RESULT_NOT_FOUND";
        private const string WaitingReviewStatus = "waiting_review";
        private const string CompletedStatus = "Completed";
        private readonly ITestResultRepository _repo;
        private readonly IAppointmentRepository _appointmentRepo;
        private readonly INotificationRepository _notificationRepo;
        private readonly ILogger<TestResultService> _logger;

        /// <summary>Creates the service.</summary>
        public TestResultService(
            ITestResultRepository repo,
            IAppointmentRepository appointmentRepo,
            INotificationRepository notificationRepo,
            ILogger<TestResultService> logger)
        {
            _repo = repo;
            _appointmentRepo = appointmentRepo;
            _notificationRepo = notificationRepo;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<(IReadOnlyList<TestResultDto> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
        {
            var (items, total) = await _repo.GetPagedAsync(page, pageSize, ct);
            return (items.Select(ToDto).ToList(), total);
        }

        /// <inheritdoc />
        public async Task<TestResultDto?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var x = await _repo.GetByIdAsync(id, ct);
            return x is null ? null : ToDto(x);
        }

        /// <inheritdoc />
        public async Task<TestResultDto> CreateAsync(CreateFullTestResultDto dto, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            // Server-generated, collision-resistant id (was "TRE" + Random 6 digits).
            var resultId = $"TRE-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            var entity = new TestResult
            {
                Id = resultId,
                TestResultId = resultId,
                AppointmentId = dto.AppointmentId,
                SampleId = dto.SampleId,
                ReagentCode = dto.ReagentCode,
                Quantity = dto.Quantity,
                DeviceId = dto.DeviceId,
                ReviewedBy = dto.ReviewedBy,
                Status = dto.Status,
                Items = ToItems(dto.Parameters),
                CreatedAt = now,
                ReviewedAt = now
            };

            await _repo.AddAsync(entity, ct);
            return ToDto(entity);
        }

        /// <inheritdoc />
        public async Task UpdateAsync(string id, UpdateTestResultDto dto, CancellationToken ct = default)
        {
            var entity = await _repo.GetByIdAsync(id, ct)
                ?? throw new ResourceNotFoundException(NotFoundCode, "Không tìm thấy kết quả xét nghiệm để cập nhật.");

            var oldStatus = entity.Status;
            var changes = new TestResultFieldChanges(
                ReagentCode: dto.ReagentCode,
                Quantity: dto.Quantity != 0 ? dto.Quantity : null,
                DeviceId: dto.DeviceId,
                ReviewedBy: dto.ReviewedBy,
                ReviewedAt: dto.ReviewedAt ?? DateTime.UtcNow,
                Status: dto.Status,
                // Parameters replace the previous set in the same atomic write (was delete + re-insert rows).
                Items: dto.Parameters is { Count: > 0 } ? ToItems(dto.Parameters) : null);

            if (!await _repo.UpdateFieldsAsync(entity.Id, changes, ct))
                throw new ResourceNotFoundException(NotFoundCode, "Không tìm thấy kết quả xét nghiệm để cập nhật.");

            // Notify the patient when the result moves into "waiting_review" (former email side effect).
            if (oldStatus != WaitingReviewStatus && dto.Status == WaitingReviewStatus)
                await NotifyWaitingReviewAsync(entity, ct);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            // Soft delete: results are medical records and stay available for audit / timeline.
            if (!await _repo.SoftDeleteAsync(id, ct))
                throw new ResourceNotFoundException(NotFoundCode, "Không tìm thấy kết quả xét nghiệm để xóa.");
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<TestResultDto>> GetByAppointmentIdAsync(string appointmentId, CancellationToken ct = default)
        {
            var list = await _repo.GetByAppointmentIdAsync(appointmentId, ct);
            return list.Select(ToDto).ToList();
        }

        /// <summary>
        /// Reviews, verifies, and digitally approves entered or analyzer-imported blood test results.
        /// </summary>
        /// <param name="id">Test result identifier.</param>
        /// <param name="dto">Approval metadata including approver identity, role, and digital signature.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The approved test result data transfer object.</returns>
        /// <exception cref="ResourceNotFoundException">Thrown when target test result is not found in MongoDB.</exception>
        public async Task<TestResultDto> ApproveAsync(string id, ApproveTestResultDto dto, CancellationToken ct = default)
        {
            var entity = await _repo.GetByIdAsync(id, ct)
                ?? throw new ResourceNotFoundException(NotFoundCode, $"Test result '{id}' was not found.");

            // BUSINESS RULE: Approval transitions the result to 'Completed' and digitally seals it,
            // making it viewable by the patient and preventing subsequent unauthorized edits.
            // TODO(senior-dev, Đợt 3): enforce approver role, read-only after signing and SHA-256 seal (BA rules UC49).
            var now = DateTime.UtcNow;
            var changes = new TestResultFieldChanges(
                Status: CompletedStatus,
                ReviewedBy: !string.IsNullOrWhiteSpace(dto.ApproverName)
                    ? $"{dto.ApproverName} ({dto.ApproverRole ?? "Doctor"})"
                    : dto.ApproverId,
                ReviewedAt: now,
                DeviceId: string.IsNullOrWhiteSpace(dto.DeviceId) ? null : dto.DeviceId,
                Quantity: dto.Quantity is > 0 ? dto.Quantity : null,
                // Keep the existing ISO validation fields in sync with the legacy string status.
                ValidationStatus: ResultValidationStatus.DoctorApproved,
                ApprovedByDoctorId: dto.ApproverId,
                ApprovedAt: now);

            if (!await _repo.UpdateFieldsAsync(entity.Id, changes, ct))
                throw new ResourceNotFoundException(NotFoundCode, $"Test result '{id}' was not found.");

            // SIDE EFFECT: Automatically synchronize parent appointment status to 'completed'
            // so billing, reception, and patient portals reflect the final test state.
            if (!string.IsNullOrWhiteSpace(entity.AppointmentId))
            {
                try
                {
                    await _appointmentRepo.UpdateStatusAsync(entity.AppointmentId, AppointmentStatus.Completed, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The approval is already persisted; a failed side effect must not report the approval as failed.
                    _logger.LogWarning(ex, "Failed to update appointment status for AppointmentId {AppointmentId}", entity.AppointmentId);
                }
            }

            var approved = await _repo.GetByIdAsync(entity.Id, ct) ?? entity;
            return ToDto(approved);
        }

        private async Task NotifyWaitingReviewAsync(TestResult result, CancellationToken ct)
        {
            var appointment = await _appointmentRepo.GetByIdAsync(result.AppointmentId, ct);
            if (appointment is null || string.IsNullOrWhiteSpace(appointment.PatientId))
            {
                _logger.LogWarning("Appointment not found for TestResult {TestResultId}; waiting-review notification skipped", result.TestResultId);
                return;
            }

            await _notificationRepo.AddAsync(new Notification
            {
                NotificationId = $"NOT{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                UserId = appointment.PatientId,
                Role = "Patient",
                Title = "Kết quả xét nghiệm đang chờ duyệt",
                Message = $"Kết quả xét nghiệm của lịch hẹn {appointment.AppointmentCode} đã có và đang chờ bác sĩ duyệt.",
                Type = "result_waiting_review",
                ReferenceId = result.TestResultId,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        private static List<TestResultItem> ToItems(IEnumerable<TestParameterDto>? parameters) =>
            (parameters ?? []).Select(p => new TestResultItem
            {
                CriterionId = p.CriteriaId,
                CriterionCode = p.Abbreviation ?? string.Empty,
                CriterionName = p.ParamName,
                MeasuredValue = p.Value ?? string.Empty
            }).ToList();

        private static TestResultDto ToDto(TestResult x) => new()
        {
            TestResultId = string.IsNullOrWhiteSpace(x.TestResultId) ? x.Id : x.TestResultId,
            AppointmentId = x.AppointmentId,
            SampleId = x.SampleId,
            ReagentCode = x.ReagentCode,
            Quantity = x.Quantity.HasValue ? (int)x.Quantity.Value : 0,
            DeviceId = x.DeviceId,
            ReviewedBy = x.ReviewedBy,
            ReviewedAt = x.ReviewedAt,
            Status = x.Status,
            CreatedAt = x.CreatedAt,
            UpdatedAt = x.UpdatedAt,
            Parameters = x.Items.Select(i => new TestParameterDto
            {
                CriteriaId = i.CriterionId,
                ParamName = i.CriterionName,
                Abbreviation = i.CriterionCode,
                Value = i.MeasuredValue
            }).ToList()
        };
    }
}
