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
        private readonly ISampleRepository? _samples;
        private readonly BMS.Core.Interfaces.ITestCriterionRepository? _criteria;

        /// <summary>Creates the service.</summary>
        public TestResultService(
            ITestResultRepository repo,
            IAppointmentRepository appointmentRepo,
            INotificationRepository notificationRepo,
            ILogger<TestResultService> logger, ISampleRepository? samples = null, BMS.Core.Interfaces.ITestCriterionRepository? criteria = null)
        {
            _repo = repo;
            _appointmentRepo = appointmentRepo;
            _notificationRepo = notificationRepo;
            _logger = logger;
            _samples = samples;
            _criteria = criteria;
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
            if (_samples is null || _criteria is null || string.IsNullOrWhiteSpace(dto.ActorId))
                throw new DomainValidationException("INVALID_RESULT_ACTOR", "Không thể xác thực người nhập kết quả.");
            var sample = await _samples.FindByCodeAsync(dto.SampleId.Trim(), ct)
                ?? throw new ResourceNotFoundException("SAMPLE_NOT_FOUND", "Không tìm thấy mẫu xét nghiệm.");
            if (sample.IsDeleted || sample.Status is not (SampleStatus.Received or SampleStatus.Processing or SampleStatus.Testing))
                throw new ConflictStateException("SAMPLE_NOT_READY", "Mẫu chưa được tiếp nhận hoặc không còn cho phép nhập kết quả.");
            var order = await _appointmentRepo.GetByIdAsync(sample.AppointmentId, ct)
                ?? await _appointmentRepo.GetByCodeAsync(sample.AppointmentId, ct)
                ?? throw new ResourceNotFoundException("APPOINTMENT_NOT_FOUND", "Không tìm thấy lịch hẹn liên kết với mẫu.");
            if (order.Status == AppointmentStatus.Cancelled || (!string.IsNullOrWhiteSpace(dto.AppointmentId)
                && dto.AppointmentId != order.Id && dto.AppointmentId != order.AppointmentCode))
                throw new DomainValidationException("INVALID_RESULT_ORDER", "Lịch hẹn không khớp với mẫu xét nghiệm.");
            if (await _repo.AnySignedForOrderAsync([sample.Id, sample.Barcode], [order.Id, order.AppointmentCode], ct))
                throw new ConflictStateException("RESULT_ALREADY_SIGNED", "Kết quả đã ký không được nhập lại.");
            var items = await ValidatedItemsAsync(dto.Parameters, ct);
            var panic = items.Any(x => x.Flag == ResultFlag.CriticalPanic);
            if (panic && (!dto.ConfirmRerunChecked || !dto.CriticalNotified || string.IsNullOrWhiteSpace(dto.DoctorNotifiedName)))
                throw new DomainValidationException("PANIC_CONFIRMATION_REQUIRED", "Cần xác nhận kiểm tra lại và thông báo bác sĩ cho kết quả panic.");
            var now = DateTime.UtcNow;
            // Server-generated, collision-resistant id (was "TRE" + Random 6 digits).
            var resultId = $"TRE-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            var entity = new TestResult
            {
                Id = resultId,
                TestResultId = resultId,
                AppointmentId = order.Id,
                SampleId = sample.Id,
                PatientId = sample.PatientId,
                PackageId = order.PackageId,
                ReagentCode = dto.ReagentCode,
                Quantity = dto.Quantity,
                DeviceId = dto.DeviceId,
                ReviewedBy = dto.ActorId,
                EnteredByUserId = dto.ActorId,
                EnteredAt = now,
                Status = WaitingReviewStatus,
                ValidationStatus = ResultValidationStatus.Entered,
                IsPanic = panic,
                Items = items,
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
            ThrowIfSigned(entity);

            if (!string.IsNullOrEmpty(dto.Status) && dto.Status != WaitingReviewStatus)
                throw new DomainValidationException("INVALID_RESULT_STATUS", "Trạng thái phê duyệt chỉ được đổi qua luồng ký duyệt.");

            var oldStatus = entity.Status;
            var measurements = dto.Parameters is { Count: > 0 } ? await ValidatedItemsAsync(dto.Parameters, ct) : null;
            var panic = measurements?.Any(x => x.Flag == ResultFlag.CriticalPanic);
            if (panic == true && (!dto.ConfirmRerunChecked || !dto.CriticalNotified || string.IsNullOrWhiteSpace(dto.DoctorNotifiedName)))
                throw new DomainValidationException("PANIC_CONFIRMATION_REQUIRED", "Cần xác nhận kiểm tra lại và thông báo bác sĩ cho kết quả panic.");
            var changes = new TestResultFieldChanges(
                ReagentCode: dto.ReagentCode,
                Quantity: dto.Quantity != 0 ? dto.Quantity : null,
                DeviceId: dto.DeviceId,
                ReviewedBy: dto.ReviewedBy,
                ReviewedAt: dto.ReviewedAt ?? DateTime.UtcNow,
                Status: dto.Status,
                // Parameters replace the previous set in the same atomic write (was delete + re-insert rows).
                Items: measurements, IsPanic: panic);

            if (!await _repo.UpdateFieldsAsync(entity.Id, changes, ct))
                throw await NotUpdatedAsync(entity.Id, "Không tìm thấy kết quả xét nghiệm để cập nhật.", ct);

            // Notify the patient when the result moves into "waiting_review" (former email side effect).
            if (oldStatus != WaitingReviewStatus && dto.Status == WaitingReviewStatus)
                await NotifyWaitingReviewAsync(entity, ct);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            // Soft delete: results are medical records and stay available for audit / timeline.
            var entity = await _repo.GetByIdAsync(id, ct)
                ?? throw new ResourceNotFoundException(NotFoundCode, "Không tìm thấy kết quả xét nghiệm để xóa.");
            ThrowIfSigned(entity);
            if (!await _repo.SoftDeleteAsync(entity.Id, ct))
                throw await NotUpdatedAsync(entity.Id, "Không tìm thấy kết quả xét nghiệm để xóa.", ct);
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
            ThrowIfSigned(entity); // a signature is final; re-approving would overwrite the signer

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
                throw await NotUpdatedAsync(entity.Id, $"Test result '{id}' was not found.", ct);

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

        /// <summary>Quy trình Đợt 3A Q5: a signed result is read-only (409 RESULT_SIGNED_READ_ONLY).</summary>
        private static void ThrowIfSigned(TestResult entity)
        {
            if (TestResultRules.IsSigned(entity))
                throw new ConflictStateException("RESULT_SIGNED_READ_ONLY", "Kết quả đã được ký duyệt, chỉ được xem, không được sửa hay xóa.");
        }

        // The repository refuses writes to signed rows atomically; tell "signed meanwhile" apart from "not found".
        private async Task<AppException> NotUpdatedAsync(string id, string notFoundMessage, CancellationToken ct)
        {
            var current = await _repo.GetByIdAsync(id, ct);
            return current != null && TestResultRules.IsSigned(current)
                ? new ConflictStateException("RESULT_SIGNED_READ_ONLY", "Kết quả đã được ký duyệt, chỉ được xem, không được sửa hay xóa.")
                : new ResourceNotFoundException(NotFoundCode, notFoundMessage);
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

        /// <summary>Evaluates measurements against trusted, unit-specific criterion master data, not client flags.</summary>
        private async Task<List<TestResultItem>> ValidatedItemsAsync(List<TestParameterDto> parameters, CancellationToken ct)
        {
            if (_criteria is null || parameters.Count is < 1 or > 200)
                throw new DomainValidationException("INVALID_RESULT_PARAMETERS", "Cần cung cấp từ 1 đến 200 chỉ số hợp lệ.");
            var items = new List<TestResultItem>();
            foreach (var p in parameters)
            {
                var criterion = await _criteria.GetByCodeAsync(p.Abbreviation ?? p.CriteriaId, ct)
                    ?? await _criteria.GetByIdAsync(p.CriteriaId, ct);
                if (criterion is null || !criterion.IsActive || criterion.IsDeleted || string.IsNullOrWhiteSpace(p.Value))
                    throw new DomainValidationException("INVALID_RESULT_CRITERION", "Chỉ tiêu không tồn tại, đã ngừng dùng hoặc thiếu giá trị.");
                var flag = ResultFlag.Normal;
                if (!string.Equals(criterion.ValueType, "Qualitative", StringComparison.OrdinalIgnoreCase))
                {
                    if (!double.TryParse(p.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                        throw new DomainValidationException("INVALID_MEASURED_VALUE", "Chỉ số định lượng phải là số hữu hạn.");
                    flag = criterion.PanicLow.HasValue && value < criterion.PanicLow || criterion.PanicHigh.HasValue && value > criterion.PanicHigh ? ResultFlag.CriticalPanic
                        : criterion.NormalMin.HasValue && value < criterion.NormalMin ? ResultFlag.Low
                        : criterion.NormalMax.HasValue && value > criterion.NormalMax ? ResultFlag.High : ResultFlag.Normal;
                }
                items.Add(new TestResultItem { CriterionId = criterion.Id, CriterionCode = criterion.CriterionCode,
                    CriterionName = criterion.CriterionName, MeasuredValue = p.Value.Trim(), Unit = criterion.Unit,
                    ReferenceInterval = criterion.NormalText ?? $"{criterion.NormalMin}-{criterion.NormalMax}", Flag = flag });
            }
            return items;
        }

        private static TestResultDto ToDto(TestResult x) => new()
        {
            IsPanic = x.IsPanic,
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
                Value = i.MeasuredValue,
                Flag = i.Flag.ToString(),
                OriginalFlag = i.OriginalFlag
            }).ToList()
        };
    }
}
