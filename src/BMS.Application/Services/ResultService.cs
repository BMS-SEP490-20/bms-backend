using BMS.Core.Entities;
using BMS.TestResults.DTOs;
using BMS.Application.Interfaces;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;


public class ResultService : IResultService
{
    private readonly IResultRepository _repository;

    public ResultService(IResultRepository repository) => _repository = repository;

    /// <summary>
    /// Saves TestResult with Items + ResultFlag. Requires confirmRerun when CriticalPanic.
    /// Status → Entered / TechnicianVerified path uses ValidationStatus.Entered.
    /// </summary>
    public async Task<EnterResultResponse> EnterAsync(
        EnterResultRequest request, string technicianId, CancellationToken ct = default)
    {
        var sample = await _repository.GetSampleAsync(request.SampleId.Trim(), ct)
            ?? throw new InvalidOperationException("MSG-ERR-RESULT-01: Không tìm thấy mẫu.");

        if (string.Equals(sample.StatusString, "Rejected", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MSG-ERR-RESULT-02: Mẫu đã bị từ chối — không nhập kết quả.");

        var items = new List<TestResultItem>();
        var anyPanic = false;

        foreach (var p in request.Parameters)
        {
            var flag = PanicEvaluator.Evaluate(p.Code, p.Value, p.RefLow, p.RefHigh);
            if (flag == ResultFlag.CriticalPanic) anyPanic = true;

            var refInterval = p.RefLow.HasValue && p.RefHigh.HasValue
                ? $"{p.RefLow}-{p.RefHigh}"
                : string.Empty;

            items.Add(new TestResultItem
            {
                CriterionId = p.Code,
                CriterionCode = p.Code,
                CriterionName = p.Name,
                MeasuredValue = p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Unit = p.Unit,
                ReferenceInterval = refInterval,
                Flag = flag
            });
        }

        if (anyPanic && !request.ConfirmRerunChecked)
        {
            throw new InvalidOperationException(
                "MSG-ERR-RESULT-03: Phát hiện chỉ số PANIC. Bắt buộc xác nhận đã kiểm tra lại / chạy lần 2 (confirmRerunChecked=true).");
        }

        var code = await _repository.GenerateNextResultCodeAsync(DateTime.UtcNow, ct);
        var entity = new TestResult
        {
            ResultCode = code,
            SampleId = sample.Id,
            PackageId = request.PackageId,
            PatientId = sample.PatientId,
            Items = items,
            ValidationStatus = ResultValidationStatus.Entered,
            EnteredByUserId = technicianId,
            EnteredAt = DateTime.UtcNow,
            ClinicalComment = request.Notes,
            IsPanic = anyPanic,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.InsertAsync(entity, ct);
        await _repository.UpdateSampleStatusAsync(sample.Id, "Testing", ct);

        return new EnterResultResponse
        {
            ResultId = entity.Id,
            ResultCode = entity.ResultCode,
            SampleId = entity.SampleId,
            Status = entity.ValidationStatus.ToString(),
            IsPanic = entity.IsPanic,
            Parameters = items.Select(x => new ParameterResultDto
            {
                Code = x.CriterionCode,
                Name = x.CriterionName,
                Value = double.TryParse(x.MeasuredValue, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0,
                Unit = x.Unit,
                Flag = x.Flag.ToString(),
                IsPanic = x.Flag == ResultFlag.CriticalPanic
            }).ToList(),
            Message = anyPanic
                ? "MSG-WRN-02: Đã lưu kết quả có chỉ số PANIC — chờ LabHead duyệt và cảnh báo bác sĩ."
                : "Lưu kết quả thành công — chờ thẩm định (ValidationStatus=Entered)."
        };
    }
}
