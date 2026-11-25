using System.Security.Cryptography;
using System.Text;
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
        private const string ApprovedStatus = "APPROVED";
        private const string CompletedStatus = "Completed";
        private readonly ITestResultRepository _repo;
        private readonly IAppointmentRepository _appointmentRepo;
        private readonly INotificationRepository _notificationRepo;
        private readonly ILogger<TestResultService> _logger;
        private readonly ISampleRepository? _samples;
        private readonly BMS.Core.Interfaces.ITestCriterionRepository? _criteria;
        private readonly ITestOrderRepository? _testOrders;
        private readonly IInventoryRepository? _inventory;

        /// <summary>Creates the service.</summary>
        public TestResultService(
            ITestResultRepository repo,
            IAppointmentRepository appointmentRepo,
            INotificationRepository notificationRepo,
            ILogger<TestResultService> logger,
            ISampleRepository? samples = null,
            BMS.Core.Interfaces.ITestCriterionRepository? criteria = null,
            ITestOrderRepository? testOrders = null,
            IInventoryRepository? inventory = null)
        {
            _repo = repo;
            _appointmentRepo = appointmentRepo;
            _notificationRepo = notificationRepo;
            _logger = logger;
            _samples = samples;
            _criteria = criteria;
            _testOrders = testOrders;
            _inventory = inventory;
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
            // Testing + RerunRequested: cho nhập/nhập lại; Received/Processing: demo path; Collected chỉ khi chưa tách QC
            if (sample.IsDeleted || sample.Status is not (
                SampleStatus.Collected or SampleStatus.Received or SampleStatus.Processing
                or SampleStatus.Testing or SampleStatus.RerunRequested))
                throw new ConflictStateException("SAMPLE_NOT_READY", "Mẫu chưa được lấy hoặc không còn cho phép nhập kết quả.");
            // Resolve linked Appointment or UC32 TestOrder (supports both appointment-based and direct lab collection samples).
            Appointment? appointment = !string.IsNullOrWhiteSpace(sample.AppointmentId)
                ? await _appointmentRepo.GetByIdAsync(sample.AppointmentId, ct)
                  ?? await _appointmentRepo.GetByCodeAsync(sample.AppointmentId, ct)
                : null;

            TestOrder? testOrder = null;
            if (appointment is null && _testOrders is not null && !string.IsNullOrWhiteSpace(sample.AppointmentId))
            {
                testOrder = await _testOrders.GetByIdAsync(sample.AppointmentId, ct)
                    ?? await _testOrders.GetByOrderNumberAsync(sample.AppointmentId, ct);
            }

            if (appointment is null && testOrder is null && string.IsNullOrWhiteSpace(sample.AppointmentId) && !string.IsNullOrWhiteSpace(sample.PatientId))
            {
                var patientAppointments = await _appointmentRepo.GetByPatientIdAsync(sample.PatientId, ct);
                appointment = patientAppointments.FirstOrDefault(a => a.Status != AppointmentStatus.Cancelled);

                if (appointment is null && _testOrders is not null)
                {
                    var patientOrders = await _testOrders.GetAllAsync(sample.PatientId, null, null, 20, ct);
                    testOrder = patientOrders.FirstOrDefault(o => o.Status != TestOrderStatus.Cancelled);
                }
            }

            string resolvedOrderId;
            string? resolvedPackageId = sample.PackageId;
            string[] orderKeys;

            if (appointment is not null)
            {
                if (appointment.Status == AppointmentStatus.Cancelled || (!string.IsNullOrWhiteSpace(dto.AppointmentId)
                    && dto.AppointmentId != appointment.Id && dto.AppointmentId != appointment.AppointmentCode))
                    throw new DomainValidationException("INVALID_RESULT_ORDER", "Lịch hẹn không khớp với mẫu xét nghiệm.");
                resolvedOrderId = appointment.Id;
                if (!string.IsNullOrWhiteSpace(appointment.PackageId))
                    resolvedPackageId = appointment.PackageId;
                orderKeys = [appointment.Id, appointment.AppointmentCode];
            }
            else if (testOrder is not null)
            {
                if (testOrder.Status == TestOrderStatus.Cancelled || (!string.IsNullOrWhiteSpace(dto.AppointmentId)
                    && dto.AppointmentId != testOrder.Id && dto.AppointmentId != testOrder.OrderNumber))
                    throw new DomainValidationException("INVALID_RESULT_ORDER", "Phiếu chỉ định không khớp với mẫu xét nghiệm hoặc đã bị hủy.");
                resolvedOrderId = !string.IsNullOrWhiteSpace(testOrder.OrderNumber) ? testOrder.OrderNumber : testOrder.Id;
                orderKeys = [testOrder.Id, testOrder.OrderNumber];
            }
            else if (!string.IsNullOrWhiteSpace(sample.AppointmentId))
            {
                if (!string.IsNullOrWhiteSpace(dto.AppointmentId) && dto.AppointmentId != sample.AppointmentId)
                    throw new DomainValidationException("INVALID_RESULT_ORDER", "Lịch hẹn không khớp với mẫu xét nghiệm.");
                resolvedOrderId = sample.AppointmentId;
                orderKeys = [sample.AppointmentId];
            }
            else
            {
                throw new ResourceNotFoundException("APPOINTMENT_NOT_FOUND", "Không tìm thấy lịch hẹn liên kết với mẫu.");
            }

            if (await _repo.AnySignedForOrderAsync([sample.Id, sample.Barcode], orderKeys, ct))
                throw new ConflictStateException("RESULT_ALREADY_SIGNED", "Kết quả đã ký không được nhập lại.");
            var items = await ValidatedItemsAsync(dto.Parameters, ct);
            var panicItems = items.Where(x => x.Flag == ResultFlag.CriticalPanic).ToList();
            var panic = panicItems.Count > 0;
            if (panic && (!dto.ConfirmRerunChecked || !dto.CriticalNotified || string.IsNullOrWhiteSpace(dto.DoctorNotifiedName)))
            {
                var panicDetail = string.Join(", ", panicItems.Select(x => $"{x.CriterionCode}={x.MeasuredValue} {x.Unit}".Trim()));
                throw new DomainValidationException("PANIC_CONFIRMATION_REQUIRED", $"Cần xác nhận kiểm tra lại và thông báo bác sĩ cho kết quả panic ({panicDetail}).");
            }
            var now = DateTime.UtcNow;
            Reagent? selectedLot = null;
            if (_inventory != null && !string.IsNullOrWhiteSpace(dto.ReagentCode))
            {
                if (dto.Quantity <= 0)
                    throw new DomainValidationException("INVALID_REAGENT_QUANTITY", "Số lượng vật tư/hóa chất tiêu hao phải lớn hơn 0.");

                var reagentKey = dto.ReagentCode.Trim();
                selectedLot = await _inventory.GetAsync(reagentKey, ct);
                if (selectedLot is null)
                {
                    var pageResult = await _inventory.ListAsync(new InventorySearch(reagentKey, null, null, null, 1, 50), now.Date, ct);
                    var matchingByCode = pageResult.Items
                        .Where(x => string.Equals(x.Code, reagentKey, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    selectedLot = matchingByCode.FirstOrDefault(x =>
                            (!x.ExpiryDate.HasValue || x.ExpiryDate.Value.Date >= now.Date) && x.Quantity >= dto.Quantity)
                        ?? matchingByCode.FirstOrDefault(x => !x.ExpiryDate.HasValue || x.ExpiryDate.Value.Date >= now.Date)
                        ?? matchingByCode.FirstOrDefault();
                }

                if (selectedLot is null)
                    throw new DomainValidationException("REAGENT_NOT_FOUND", $"Không tìm thấy lô hóa chất/vật tư '{reagentKey}' trong kho.");
                if (selectedLot.ExpiryDate.HasValue && selectedLot.ExpiryDate.Value.Date < now.Date)
                    throw new DomainValidationException("REAGENT_EXPIRED", $"Lô hóa chất {selectedLot.Code} ({selectedLot.LotNumber}) đã hết hạn sử dụng.");
                if (selectedLot.Quantity < dto.Quantity)
                    throw new DomainValidationException("INSUFFICIENT_REAGENT_STOCK", $"Tồn kho lô {selectedLot.Code} ({selectedLot.LotNumber}) không đủ: còn {selectedLot.Quantity} {selectedLot.Unit}, cần {dto.Quantity} {selectedLot.Unit}.");
            }

            // Server-generated, collision-resistant id (was "TRE" + Random 6 digits).
            var resultId = $"TRE-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            var entity = new TestResult
            {
                Id = resultId,
                TestResultId = resultId,
                AppointmentId = resolvedOrderId,
                SampleId = sample.Id,
                PatientId = sample.PatientId,
                PackageId = resolvedPackageId,
                ReagentCode = selectedLot?.Code ?? dto.ReagentCode,
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

            // Auto-consume reagent stock (BR-LAB-05 / UC56) when ReagentCode and Quantity > 0 are provided
            if (_inventory != null && selectedLot != null && dto.Quantity > 0)
            {
                try
                {
                    var currentLot = selectedLot;
                    for (var attempt = 0; attempt < 2; attempt++)
                    {
                        if (currentLot.Quantity < dto.Quantity) break;
                        var nextQty = Math.Max(0m, currentLot.Quantity - dto.Quantity);
                        var nextLot = new Reagent
                        {
                            Id = currentLot.Id,
                            Code = currentLot.Code,
                            LotNumber = currentLot.LotNumber,
                            Unit = currentLot.Unit,
                            Name = currentLot.Name,
                            Manufacturer = currentLot.Manufacturer,
                            Quantity = nextQty,
                            MinimumStock = currentLot.MinimumStock,
                            ExpiryDate = currentLot.ExpiryDate,
                            OpenedDate = currentLot.OpenedDate ?? now,
                            StorageCondition = currentLot.StorageCondition,
                            ImportBatchNumber = currentLot.ImportBatchNumber
                        };
                        var change = new InventoryChange
                        {
                            Action = "CONSUME_TEST_RUN",
                            ActorId = dto.ActorId,
                            ActorName = dto.ActorId,
                            Reason = $"Tiêu hao {dto.Quantity} {currentLot.Unit} cho kết quả {resultId} (Mẫu {sample.Barcode})",
                            At = now,
                            Before = new InventorySnapshot(currentLot.Name, currentLot.Manufacturer, currentLot.Quantity, currentLot.MinimumStock, currentLot.ExpiryDate),
                            After = new InventorySnapshot(nextLot.Name, nextLot.Manufacturer, nextLot.Quantity, nextLot.MinimumStock, nextLot.ExpiryDate)
                        };
                        var updated = await _inventory.UpdateAsync(nextLot, currentLot.Revision, change, ct);
                        if (updated != null) break;
                        var reloaded = await _inventory.GetAsync(currentLot.Id, ct);
                        if (reloaded is null) break;
                        currentLot = reloaded;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể trừ tồn kho hóa chất {ReagentCode} khi lưu kết quả {ResultId}.", dto.ReagentCode, resultId);
                }
            }

            // Cập nhật trạng thái mẫu sang Completed và lịch hẹn sang InProgress (chờ bác sĩ kết luận)
            sample.Status = SampleStatus.Completed;
            sample.StatusString = SampleStatus.Completed.ToString();
            await _samples.UpdateAsync(sample, ct);

            if (appointment != null && !string.IsNullOrWhiteSpace(appointment.Id))
            {
                try
                {
                    await _appointmentRepo.UpdateStatusAsync(appointment.Id, AppointmentStatus.InProgress, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể cập nhật trạng thái lịch hẹn {OrderId} sang InProgress.", appointment.Id);
                }
            }

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
            ThrowIfSigned(entity); // clinical seal is final; re-approving would overwrite the signer

            // GATE (main flow step 4→5): only Doctor clinical approve publishes to patient portal.
            // Requires existing Lab-entered result; Manager tech-verify is optional and does not publish.
            if (string.IsNullOrWhiteSpace(dto.ApproverId))
                throw new DomainValidationException("MSG-ERR-DOC-APPROVE-01", "Thiếu định danh bác sĩ ký duyệt (ApproverId).");

            var now = DateTime.UtcNow;
            var sealPayload = $"BMS-CLINICAL-APPROVE:{entity.Id}:{dto.ApproverId}:{now:yyyyMMddHHmmss}";
            var digitalSignature = "SHA256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sealPayload))).ToLowerInvariant();

            var icd10Code = string.IsNullOrWhiteSpace(dto.Icd10Code) ? null : dto.Icd10Code.Trim();
            var finalDiagnosis = string.IsNullOrWhiteSpace(dto.FinalDiagnosis) ? null : dto.FinalDiagnosis.Trim();
            var doctorConclusion = string.IsNullOrWhiteSpace(dto.DoctorConclusion) ? null : dto.DoctorConclusion.Trim();
            var treatmentAdvice = string.IsNullOrWhiteSpace(dto.TreatmentAdvice) ? null : dto.TreatmentAdvice.Trim();
            var followUpDate = string.IsNullOrWhiteSpace(dto.FollowUpDate) ? null : dto.FollowUpDate.Trim();
            var clinicalComment = doctorConclusion ?? (string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim());

            var changes = new TestResultFieldChanges(
                Status: ApprovedStatus,
                ReviewedBy: !string.IsNullOrWhiteSpace(dto.ApproverName)
                    ? $"{dto.ApproverName} ({dto.ApproverRole ?? "Doctor"})"
                    : dto.ApproverId,
                ReviewedAt: now,
                DeviceId: string.IsNullOrWhiteSpace(dto.DeviceId) ? null : dto.DeviceId,
                Quantity: dto.Quantity is > 0 ? dto.Quantity : null,
                ValidationStatus: ResultValidationStatus.DoctorApproved,
                ApprovedByDoctorId: dto.ApproverId,
                ApprovedAt: now,
                DigitalSignature: digitalSignature,
                ClinicalComment: clinicalComment,
                Icd10Code: icd10Code,
                FinalDiagnosis: finalDiagnosis,
                DoctorConclusion: doctorConclusion,
                TreatmentAdvice: treatmentAdvice,
                FollowUpDate: followUpDate);

            if (!await _repo.UpdateFieldsAsync(entity.Id, changes, ct))
                throw await NotUpdatedAsync(entity.Id, $"Test result '{id}' was not found.", ct);

            if (!string.IsNullOrWhiteSpace(entity.AppointmentId))
            {
                try
                {
                    await _appointmentRepo.UpdateStatusAsync(entity.AppointmentId, AppointmentStatus.Completed, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Failed to update appointment status for AppointmentId {AppointmentId}", entity.AppointmentId);
                }
            }

            await NotifyPatientResultAvailableAsync(entity, dto, ct);

            var approved = await _repo.GetByIdAsync(entity.Id, ct) ?? entity;
            return ToDto(approved);
        }

        /// <inheritdoc />
        public async Task<TestResultDto> UpdateClinicalConsultationAsync(string id, UpdateClinicalConsultationDto dto, CancellationToken ct = default)
        {
            var entity = await _repo.GetByIdAsync(id, ct)
                ?? throw new ResourceNotFoundException(NotFoundCode, $"Test result '{id}' was not found.");

            var icd10Code = string.IsNullOrWhiteSpace(dto.Icd10Code) ? null : dto.Icd10Code.Trim();
            var finalDiagnosis = string.IsNullOrWhiteSpace(dto.FinalDiagnosis) ? null : dto.FinalDiagnosis.Trim();
            var doctorConclusion = string.IsNullOrWhiteSpace(dto.DoctorConclusion) ? null : dto.DoctorConclusion.Trim();
            var treatmentAdvice = string.IsNullOrWhiteSpace(dto.TreatmentAdvice) ? null : dto.TreatmentAdvice.Trim();
            var followUpDate = string.IsNullOrWhiteSpace(dto.FollowUpDate) ? null : dto.FollowUpDate.Trim();

            if (!await _repo.UpdateClinicalConsultationAsync(entity.Id, icd10Code, finalDiagnosis, doctorConclusion, treatmentAdvice, followUpDate, ct))
                throw new ResourceNotFoundException(NotFoundCode, $"Test result '{id}' was not found.");

            var updated = await _repo.GetByIdAsync(entity.Id, ct) ?? entity;
            return ToDto(updated);
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

            // Lab finished entry → notify Doctor to thẩm định (main flow step 3→4). Patient does not get values yet.
            await _notificationRepo.AddAsync(new Notification
            {
                NotificationId = $"NOT{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                UserId = appointment.DoctorId ?? appointment.PatientId,
                Role = "Doctor",
                Title = "KQ Lab sẵn sàng — chờ BS thẩm định",
                Message = $"Lab đã nhập KQ cho lịch {appointment.AppointmentCode}. Vào /doctor/console → Thẩm Định & Tư Vấn → Ký Số & Trả Kết Quả Cho Bệnh Nhân.",
                Type = "result_waiting_review",
                ReferenceId = result.TestResultId,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        private async Task NotifyPatientResultAvailableAsync(TestResult result, ApproveTestResultDto dto, CancellationToken ct)
        {
            string? patientId = null;
            string? appointmentCode = null;
            if (!string.IsNullOrWhiteSpace(result.AppointmentId))
            {
                var appointment = await _appointmentRepo.GetByIdAsync(result.AppointmentId, ct);
                patientId = appointment?.PatientId;
                appointmentCode = appointment?.AppointmentCode;
            }
            if (string.IsNullOrWhiteSpace(patientId))
                patientId = result.PatientId;
            if (string.IsNullOrWhiteSpace(patientId))
            {
                _logger.LogWarning("No patient id for TestResult {TestResultId}; patient publish notification skipped", result.Id);
                return;
            }

            await _notificationRepo.AddAsync(new Notification
            {
                NotificationId = $"NOT{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
                UserId = patientId,
                Role = "Patient",
                Title = "Kết quả xét nghiệm đã sẵn sàng",
                Message = $"Bác sĩ {dto.ApproverName ?? dto.ApproverId} đã thẩm định và duyệt kết quả{(string.IsNullOrWhiteSpace(appointmentCode) ? "" : $" của lịch {appointmentCode}")}. Vào /patient/test-history để xem.",
                Type = "TestResultAvailable",
                ReferenceId = result.TestResultId ?? result.Id,
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
                var lookupCode = !string.IsNullOrWhiteSpace(p.Abbreviation) ? p.Abbreviation.Trim() : (p.CriteriaId?.Trim() ?? string.Empty);
                var criterion = (!string.IsNullOrWhiteSpace(lookupCode) ? await _criteria.GetByCodeAsync(lookupCode, ct) : null)
                    ?? (!string.IsNullOrWhiteSpace(p.CriteriaId) ? await _criteria.GetByIdAsync(p.CriteriaId.Trim(), ct) : null);
                if (criterion is null || !criterion.IsActive || criterion.IsDeleted || string.IsNullOrWhiteSpace(p.Value))
                    throw new DomainValidationException("INVALID_RESULT_CRITERION", $"Chỉ tiêu '{lookupCode}' không tồn tại, đã ngừng dùng hoặc thiếu giá trị.");
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
            ValidationStatus = x.ValidationStatus.ToString(),
            ApprovedByDoctorId = x.ApprovedByDoctorId,
            ApprovedAt = x.ApprovedAt,
            DigitalSignature = x.DigitalSignature,
            ClinicalComment = x.ClinicalComment,
            Icd10Code = x.Icd10Code,
            FinalDiagnosis = x.FinalDiagnosis,
            DoctorConclusion = x.DoctorConclusion,
            TreatmentAdvice = x.TreatmentAdvice,
            FollowUpDate = x.FollowUpDate,
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
