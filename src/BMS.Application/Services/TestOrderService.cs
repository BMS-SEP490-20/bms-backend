using System.Text.RegularExpressions;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using BMS.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

/// <inheritdoc />
/// <remarks>
/// WHY rewritten (Đợt 3): the old service trusted DoctorId and Price from the body, defaulted the price to the
/// client value, had no ICD-10, put every order straight into "Ordered" and pre-created specimen slots in the
/// in-memory BmsDbContext before payment. Rules now: TIEU-CHI Đợt 3 Bước 1 and QĐ10.
/// </remarks>
public sealed partial class TestOrderService(
    ITestOrderRepository orders,
    IPatientRepository patients,
    IUserRepository users,
    IServicePackageRepository packages,
    ISequenceGenerator sequences,
    TimeProvider clock,
    ILogger<TestOrderService> logger) : ITestOrderService
{
    /// <summary>ICD-10 format accepted (GAP C4; QĐ C7: format only, no WHO list lookup).</summary>
    [GeneratedRegex(@"^[A-Z][0-9]{2}(\.[0-9]{1,2})?$")]
    public static partial Regex Icd10();

    private const int MaxOrdersPerDay = 9999;

    /// <inheritdoc />
    public async Task<IReadOnlyList<TestCatalogItemDto>> GetAvailableCatalogAsync(CancellationToken ct = default)
    {
        var paged = await packages.SearchAsync(new ServicePackageQuery(null, true, null, null, "Code", false, 1, 200), ct);
        return paged.Items.Select(pkg =>
        {
            var tube = TubeRules.For(pkg.Code, pkg.Name, pkg.Category);
            return new TestCatalogItemDto
            {
                Code = pkg.Code,
                Name = pkg.Name,
                Category = pkg.Category ?? string.Empty,
                TubeType = tube.TubeType,
                TubeColor = tube.Color,
                VolumeMl = 0,
                TurnaroundTime = string.Empty,
                PreparationNotes = pkg.PreparationNotes ?? string.Empty,
                FastingRequired = IsFasting(pkg.PreparationNotes),
                FastingHours = FastingHours(pkg.PreparationNotes),
                Price = pkg.Price
            };
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TestOrderResponseDto>> GetOrdersAsync(string? patientId = null, string? doctorId = null,
        string? status = null, CancellationToken ct = default)
    {
        TestOrderStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<TestOrderStatus>(status, true, out var s) || !Enum.IsDefined(s))
                throw new DomainValidationException("ORDER_STATUS_INVALID", "Trạng thái đơn không hợp lệ.");
            parsed = s;
        }
        var list = await orders.GetAllAsync(patientId, doctorId, parsed, 200, ct);
        return list.Select(Map).ToList();
    }

    /// <inheritdoc />
    public async Task<TestOrderResponseDto?> GetOrderByIdAsync(string id, CancellationToken ct = default)
    {
        var order = await FindAsync(id, ct);
        return order == null ? null : Map(order);
    }

    /// <inheritdoc />
    public async Task<TestOrderResponseDto> CreateTestOrderAsync(CreateTestOrderRequestDto request, string doctorUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(doctorUserId)) throw new ForbiddenAccessException();

        // 1. ICD-10 first: a missing (non-draft) or malformed code must reject the order before anything is looked up
        //    or saved. Quy trình Đợt 3A Q4: a draft may be saved without ICD-10; it is required on submit.
        var icd = request.SaveAsDraft
            ? NormalizeIcd10Codes(request.Icd10Codes, request.Icd10Code, required: false)
            : NormalizeIcd10(request);

        if (request.Items == null || request.Items.Count == 0)
            throw new DomainValidationException("ORDER_ITEMS_REQUIRED", "Vui lòng chọn ít nhất một xét nghiệm.");
        var priority = ParsePriority(request.Priority);

        var patient = await patients.GetByIdOrCodeAsync(request.PatientId.Trim(), ct)
                      ?? throw new ResourceNotFoundException("PATIENT_NOT_FOUND", "Không tìm thấy bệnh nhân.");

        // 2. Prices only from the server catalog (one query, no N+1). Unknown code → error, never a default price.
        var codes = request.Items.Select(i => (i.TestCode ?? string.Empty).Trim().ToUpperInvariant())
            .Where(c => c.Length > 0).Distinct().ToList();
        if (codes.Count == 0)
            throw new DomainValidationException("ORDER_ITEMS_REQUIRED", "Vui lòng chọn ít nhất một xét nghiệm.");
        var catalog = (await packages.GetActiveByCodesAsync(codes, ct)).ToDictionary(p => p.Code, StringComparer.Ordinal);
        var missing = codes.Where(c => !catalog.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new DomainValidationException("ORDER_TEST_NOT_IN_CATALOG",
                $"Không tìm thấy xét nghiệm trong danh mục: {string.Join(", ", missing)}.");

        var items = codes.Select(c =>
        {
            var pkg = catalog[c];
            var tube = TubeRules.For(pkg.Code, pkg.Name, pkg.Category);
            return new TestOrderItem
            {
                TestCode = pkg.Code,
                TestName = pkg.Name,
                Category = pkg.Category ?? string.Empty,
                TubeType = tube.TubeType,
                TubeColor = tube.Color,
                VolumeMl = 0,
                TurnaroundTime = string.Empty,
                PreparationNotes = pkg.PreparationNotes ?? string.Empty,
                Price = pkg.Price
            };
        }).ToList();

        var doctor = await users.GetByIdAsync(doctorUserId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var localDate = ClinicTime.LocalDate(now);
        var seq = await sequences.NextAsync($"order:{localDate:yyyy-MM-dd}", ct);
        if (seq > MaxOrdersPerDay) throw new SequenceExhaustedException("ORD-");

        var isStat = priority == SamplePriority.Stat;
        var order = new TestOrder
        {
            OrderNumber = $"ORD-{localDate:yyyyMMdd}-{seq:D4}",
            PatientId = patient.Id,
            PatientName = patient.FullName,
            PatientGender = patient.GenderString,
            PatientAge = AgeOn(patient.DateOfBirth, localDate),
            DoctorId = doctorUserId,
            DoctorName = doctor?.FullName ?? string.Empty,
            Priority = priority,
            // WHY: Routine/Urgent wait for payment (BA:L38); STAT skips the payment gate (post-check) but still
            // owes the fee, so it stays AwaitingPayment with PaymentDeferred = true.
            Status = request.SaveAsDraft ? TestOrderStatus.Draft : TestOrderStatus.AwaitingPayment,
            PaymentDeferred = isStat && !request.SaveAsDraft,
            Icd10Codes = icd,
            ClinicalIndication = request.ClinicalIndication.Trim(),
            SpecialInstructions = string.IsNullOrWhiteSpace(request.SpecialInstructions) ? null : request.SpecialInstructions.Trim(),
            ScheduledDate = request.ScheduledDate ?? localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            TotalAmount = items.Sum(i => i.Price),
            FastingRequired = items.Any(i => IsFasting(i.PreparationNotes)),
            FastingHours = items.Select(i => FastingHours(i.PreparationNotes)).DefaultIfEmpty(0).Max(),
            Items = items,
            CreatedBy = doctorUserId,
            CreatedAt = now
        };
        await orders.InsertAsync(order, ct);
        logger.LogInformation("Test order {OrderNumber} created by doctor {DoctorId} for {Patient} ({Priority}, {Status}, {Count} tests)",
            order.OrderNumber, doctorUserId, PiiMasker.MaskName(order.PatientName), order.Priority, order.Status, items.Count);
        return Map(order);
    }

    /// <inheritdoc />
    public async Task<TestOrderResponseDto> UpdateOrderStatusAsync(string id, UpdateTestOrderStatusRequestDto request,
        string actorUserId, bool actorIsManager, CancellationToken ct = default)
    {
        if (!Enum.TryParse<TestOrderStatus>(request.Status?.Trim(), true, out var target) || !Enum.IsDefined(target))
            throw new DomainValidationException("ORDER_STATUS_INVALID", "Trạng thái đơn không hợp lệ.");
        // QĐ10: users may only cancel; payment, sampling and results move the order automatically.
        if (target != TestOrderStatus.Cancelled)
            throw new ForbiddenAccessException("Chỉ được hủy đơn; các trạng thái khác do hệ thống tự cập nhật.");

        var order = await FindAsync(id, ct) ?? throw new ResourceNotFoundException("ORDER_NOT_FOUND", "Không tìm thấy đơn xét nghiệm.");
        TestOrderStatus[] from;
        if (actorIsManager)
        {
            from = TestOrderStatusRules.ManagerCancellable;
        }
        else
        {
            if (!string.Equals(order.DoctorId, actorUserId, StringComparison.Ordinal))
                throw new ForbiddenAccessException("Bác sĩ chỉ được hủy đơn do chính mình chỉ định.");
            from = TestOrderStatusRules.BeforeCollection;
        }
        ThrowIfNotCancellable(order.Status, actorIsManager);

        var now = clock.GetUtcNow().UtcDateTime;
        var note = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        var updated = await orders.TryTransitionAsync(order.Id, from, TestOrderStatus.Cancelled, actorUserId, now, note, ct);
        if (updated == null)
        {
            // Lost a race (e.g. sample collected meanwhile): report the state that blocked us.
            var current = await orders.GetByIdAsync(order.Id, ct);
            ThrowIfNotCancellable(current?.Status ?? order.Status, actorIsManager);
            throw new ConflictStateException("ORDER_STATE_CHANGED", "Đơn vừa được cập nhật, vui lòng tải lại.");
        }
        logger.LogInformation("Test order {OrderNumber} cancelled by {ActorId} ({Role})", updated.OrderNumber, actorUserId,
            actorIsManager ? RoleNames.Manager : RoleNames.Doctor);
        return Map(updated);
    }

    /// <inheritdoc />
    public async Task<TestOrderResponseDto> SubmitDraftAsync(string id, SubmitTestOrderRequestDto request, string doctorUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(doctorUserId)) throw new ForbiddenAccessException();
        var order = await FindAsync(id, ct) ?? throw new ResourceNotFoundException("ORDER_NOT_FOUND", "Không tìm thấy đơn xét nghiệm.");
        if (!string.Equals(order.DoctorId, doctorUserId, StringComparison.Ordinal))
            throw new ForbiddenAccessException("Bác sĩ chỉ được gửi đơn nháp do chính mình tạo.");
        if (order.Status != TestOrderStatus.Draft)
            throw new ConflictStateException("ORDER_NOT_DRAFT", "Đơn không còn ở trạng thái nháp.");

        // Quy trình Đợt 3A Q4: ICD-10 is mandatory from submit on, i.e. before the fee can be calculated.
        var icd = NormalizeIcd10Codes(order.Icd10Codes.Concat(request.Icd10Codes ?? []), request.Icd10Code, required: true);
        var deferred = order.Priority == SamplePriority.Stat;
        var now = clock.GetUtcNow().UtcDateTime;
        var submitted = await orders.TrySubmitDraftAsync(order.Id, doctorUserId, icd, deferred, now, ct)
                        ?? throw new ConflictStateException("ORDER_NOT_DRAFT", "Đơn vừa được gửi hoặc hủy, vui lòng tải lại.");
        logger.LogInformation("Draft order {OrderNumber} submitted by doctor {DoctorId} ({Count} ICD-10 codes)",
            submitted.OrderNumber, doctorUserId, icd.Count);
        return Map(submitted);
    }

    private static void ThrowIfNotCancellable(TestOrderStatus status, bool manager)
    {
        if (status == TestOrderStatus.Cancelled)
            throw new ConflictStateException("ORDER_ALREADY_CANCELLED", "Đơn đã được hủy trước đó.");
        if (status == TestOrderStatus.Completed)
            throw new ConflictStateException("ORDER_COMPLETED", "Đơn đã hoàn tất, không thể hủy.");
        if (!manager && TestOrderStatusRules.IsSampleCollectedOrLater(status))
            throw new ConflictStateException("ORDER_SAMPLE_COLLECTED", "Mẫu đã được lấy, không thể hủy đơn. Vui lòng liên hệ Quản lý.");
    }

    private async Task<TestOrder?> FindAsync(string id, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(id) ? null : await orders.GetByIdAsync(id, ct) ?? await orders.GetByOrderNumberAsync(id, ct);

    /// <summary>Merges, trims and upper-cases ICD-10 codes; throws 400 MSG-ERR-DOC-01 if none or any malformed.</summary>
    public static List<string> NormalizeIcd10(CreateTestOrderRequestDto request) =>
        NormalizeIcd10Codes(request.Icd10Codes, request.Icd10Code, required: true);

    /// <summary>
    /// Merges, trims, upper-cases and de-duplicates ICD-10 codes. Any malformed code → 400 MSG-ERR-DOC-01; none at all
    /// → 400 MSG-ERR-DOC-01 only when <paramref name="required"/> (drafts may have none, Quy trình Đợt 3A Q4).
    /// </summary>
    public static List<string> NormalizeIcd10Codes(IEnumerable<string?>? codes, string? single, bool required)
    {
        var raw = (codes ?? []).Append(single)
            .Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim().ToUpperInvariant()).Distinct().ToList();
        if (raw.Count == 0 && required)
            throw new DomainValidationException("MSG-ERR-DOC-01", "Vui lòng nhập ít nhất một mã chẩn đoán ICD-10.");
        var bad = raw.Where(c => !Icd10().IsMatch(c)).ToList();
        if (bad.Count > 0)
            throw new DomainValidationException("MSG-ERR-DOC-01", $"Mã ICD-10 không đúng định dạng: {string.Join(", ", bad)} (ví dụ đúng: E11.9).");
        return raw;
    }

    private static SamplePriority ParsePriority(string? value) => (value ?? "Routine").Trim().ToUpperInvariant() switch
    {
        "" or "ROUTINE" or "NORMAL" => SamplePriority.Normal,
        "URGENT" => SamplePriority.Urgent,
        "STAT" => SamplePriority.Stat,
        _ => throw new DomainValidationException("ORDER_PRIORITY_INVALID", "Mức ưu tiên phải là Routine, Urgent hoặc STAT.")
    };

    private static int AgeOn(DateTime? dob, DateOnly today)
    {
        if (dob == null) return 0;
        var birth = DateOnly.FromDateTime(dob.Value);
        var age = today.Year - birth.Year;
        if (birth > today.AddYears(-age)) age--;
        return Math.Max(0, age);
    }

    private static bool IsFasting(string? notes) =>
        !string.IsNullOrEmpty(notes) && (notes.Contains("fast", StringComparison.OrdinalIgnoreCase) || notes.Contains("nhịn ăn", StringComparison.OrdinalIgnoreCase));

    private static int FastingHours(string? notes) =>
        !IsFasting(notes) ? 0 : notes!.Contains("12") ? 12 : notes.Contains('8') ? 8 : 0;

    private static TestOrderResponseDto Map(TestOrder o) => new()
    {
        Id = o.Id,
        OrderNumber = o.OrderNumber,
        PatientId = o.PatientId,
        PatientName = o.PatientName,
        PatientGender = o.PatientGender,
        PatientAge = o.PatientAge,
        DoctorId = o.DoctorId,
        DoctorName = o.DoctorName,
        Priority = o.Priority == SamplePriority.Normal ? "Routine" : o.Priority == SamplePriority.Stat ? "STAT" : o.Priority.ToString(),
        Status = o.Status.ToString(),
        ClinicalIndication = o.ClinicalIndication,
        Icd10Codes = o.Icd10Codes,
        PaymentDeferred = o.PaymentDeferred,
        SpecialInstructions = o.SpecialInstructions,
        ScheduledDate = o.ScheduledDate,
        TotalAmount = o.TotalAmount,
        FastingRequired = o.FastingRequired,
        FastingHours = o.FastingHours,
        CreatedAt = o.CreatedAt,
        CreatedBy = o.CreatedBy,
        Items = o.Items.Select(i => new TestOrderItemDto
        {
            TestCode = i.TestCode,
            TestName = i.TestName,
            Category = i.Category,
            TubeType = i.TubeType,
            TubeColor = i.TubeColor ?? string.Empty,
            VolumeMl = i.VolumeMl,
            TurnaroundTime = i.TurnaroundTime,
            PreparationNotes = i.PreparationNotes,
            Price = i.Price
        }).ToList()
    };
}
