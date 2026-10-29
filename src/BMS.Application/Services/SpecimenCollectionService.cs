using System.Globalization;
using System.Text;
using BMS.Application.DTOs.Specimens;
using BMS.Application.Interfaces;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using BMS.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace BMS.Application.Services;

/// <inheritdoc />
/// <remarks>WHY new (Đợt 3): barcodes were made in three formats (BC-, SPC-, random) from in-memory counts and
/// specimens were pre-created before payment; tube colors mapped NaF to Green. TIEU-CHI Đợt 3 Bước 3.</remarks>
public sealed class SpecimenCollectionService(
    ITestOrderRepository orders,
    IPatientRepository patients,
    ISpecimenRepository specimens,
    ISequenceGenerator sequences,
    TimeProvider clock,
    ILogger<SpecimenCollectionService> logger) : ISpecimenCollectionService
{
    private const int MaxBarcodesPerDay = 9999;

    /// <inheritdoc />
    public async Task<CollectSpecimenResultDto> CollectAsync(CollectSpecimenRequestDto request, string labUserId, CancellationToken ct = default)
    {
        var order = (string.IsNullOrWhiteSpace(request.OrderId) ? null
                        : await orders.GetByIdAsync(request.OrderId.Trim(), ct) ?? await orders.GetByOrderNumberAsync(request.OrderId.Trim(), ct))
                    ?? throw new ResourceNotFoundException("ORDER_NOT_FOUND", "Không tìm thấy đơn xét nghiệm.");
        EnsureCollectable(order);

        var patient = await patients.GetByIdOrCodeAsync(order.PatientId, ct)
                      ?? throw new ResourceNotFoundException("PATIENT_NOT_FOUND", "Không tìm thấy hồ sơ bệnh nhân của đơn.");
        VerifyTwoFactors(patient, request);

        var tubes = order.Items
            .GroupBy(i => TubeRules.For(i.TestCode, i.TestName, i.Category))
            .OrderBy(g => g.Key.DrawOrder)
            .ToList();
        var now = clock.GetUtcNow().UtcDateTime;
        var day = ClinicTime.LocalDate(now);
        var barcodes = new List<string>(tubes.Count);
        foreach (var _ in tubes)
        {
            var seq = await sequences.NextAsync($"smp:{day:yyyy-MM-dd}", ct);
            if (seq > MaxBarcodesPerDay) throw new SequenceExhaustedException("SMP-");
            barcodes.Add($"SMP-{day:yyyyMMdd}-{seq:D4}");
        }

        // Conditional transition = the single winner (a parallel collect or a cancel loses with 409).
        var from = order.PaymentDeferred
            ? new[] { TestOrderStatus.PaidPendingSample, TestOrderStatus.AwaitingPayment, TestOrderStatus.Ordered }
            : new[] { TestOrderStatus.PaidPendingSample };
        var collected = await orders.TryTransitionAsync(order.Id, from, TestOrderStatus.SampleCollected, labUserId, now, null, ct);
        if (collected == null)
        {
            var current = await orders.GetByIdAsync(order.Id, ct);
            if (current != null) EnsureCollectable(current);
            throw new ConflictStateException("ORDER_STATE_CHANGED", "Đơn vừa được cập nhật, vui lòng tải lại.");
        }

        var samples = tubes.Select((g, i) => new Sample
        {
            Barcode = barcodes[i],
            SmpDay = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DrawOrder = g.Key.DrawOrder,
            TestOrderId = order.OrderNumber,
            PatientId = order.PatientId,
            PatientName = order.PatientName,
            PatientGender = order.PatientGender,
            PatientAge = order.PatientAge,
            TestPackageName = string.Join(", ", g.Select(x => x.TestName).Distinct()),
            SampleType = SampleTypeFor(g.Key),
            TubeColor = g.Key.Color,
            Status = SampleStatus.Collected,
            Priority = order.Priority,
            CollectedBy = labUserId,
            CollectedAt = now,
            Notes = $"Order: {order.OrderNumber}"
        }).ToList();
        try
        {
            await specimens.InsertManyAsync(samples, ct);
        }
        catch
        {
            // WHY compensate: without tubes the order must go back to collectable, otherwise it is stuck.
            await orders.TryTransitionAsync(order.Id, [TestOrderStatus.SampleCollected], order.Status, labUserId, now, "rollback: specimen insert failed", CancellationToken.None);
            throw;
        }

        logger.LogInformation("Specimens {Barcodes} collected for order {OrderNumber} ({Patient}) by {LabUserId}",
            string.Join(",", barcodes), order.OrderNumber, PiiMasker.MaskName(order.PatientName), labUserId);
        return new CollectSpecimenResultDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            OrderStatus = collected.Status.ToString(),
            LabelPatientName = patient.FullName.ToUpper(CultureInfo.GetCultureInfo("vi-VN")),
            BirthYear = patient.DateOfBirth?.Year,
            Gender = patient.GenderString,
            CollectedAtUtc = now,
            CollectedBy = labUserId,
            Tubes = tubes.Select((g, i) => new CollectedTubeDto
            {
                Barcode = barcodes[i],
                TubeColor = g.Key.Color,
                TubeType = g.Key.TubeType,
                DrawOrder = g.Key.DrawOrder,
                Inversions = g.Key.Inversions,
                Tests = g.Select(x => x.TestCode).ToList()
            }).ToList()
        };
    }

    private static void EnsureCollectable(TestOrder order)
    {
        if (TestOrderStatusRules.CanCollectSample(order)) return;
        throw order.Status switch
        {
            TestOrderStatus.AwaitingPayment or TestOrderStatus.Ordered =>
                new ConflictStateException("ORDER_NOT_PAID", "Đơn chưa thanh toán, chưa được lấy mẫu."),
            TestOrderStatus.Cancelled => new ConflictStateException("ORDER_CANCELLED", "Đơn đã hủy."),
            TestOrderStatus.Draft => new ConflictStateException("ORDER_DRAFT", "Đơn còn nháp."),
            _ => new ConflictStateException("SAMPLE_ALREADY_COLLECTED", "Mẫu của đơn này đã được lấy.")
        };
    }

    /// <summary>BA:L41: name + (date of birth or CCCD) must both match; one wrong factor blocks collection.</summary>
    private static void VerifyTwoFactors(Patient patient, CollectSpecimenRequestDto r)
    {
        var nameOk = !string.IsNullOrWhiteSpace(r.FullName) && NormalizeName(r.FullName) == NormalizeName(patient.FullName);
        bool secondOk;
        if (r.DateOfBirth.HasValue)
            secondOk = patient.DateOfBirth.HasValue && DateOnly.FromDateTime(patient.DateOfBirth.Value) == r.DateOfBirth.Value;
        else if (!string.IsNullOrWhiteSpace(r.IdentityNumber))
            secondOk = string.Equals(r.IdentityNumber.Trim(), patient.IdentityCardNumber, StringComparison.Ordinal);
        else
            secondOk = false;
        if (!nameOk || !secondOk)
            throw new DomainValidationException("SAMPLE_PATIENT_MISMATCH",
                "Thông tin xác thực bệnh nhân không khớp (họ tên và ngày sinh/CCCD). Không được ghi nhận lấy mẫu.");
    }

    private static string NormalizeName(string s) =>
        string.Join(' ', RemoveMarks(s).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string RemoveMarks(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c == 'đ' ? 'd' : c == 'Đ' ? 'D' : c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string SampleTypeFor(TubeSpec t) => t.Color switch
    {
        "Purple" => "Whole blood (EDTA)",
        "Gray" => "Plasma (NaF)",
        "LightBlue" => "Plasma (citrate)",
        "Green" => "Plasma (heparin)",
        "BloodCulture" => "Blood culture",
        _ => "Serum"
    };
}
