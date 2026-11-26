using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.SampleCheckIn.DTOs;
using BMS.Application.DTOs.Notifications;
using BMS.Application.Interfaces;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

public class SampleCheckInService : ISampleCheckInService
{
    private static readonly HashSet<SampleStatus> AllowedInbound = new()
    {
        SampleStatus.Collected, SampleStatus.InTransit
    };

    private readonly ISampleRepository _repository;
    private readonly INotificationService? _notifications;

    public SampleCheckInService(ISampleRepository repository, INotificationService? notifications = null)
    {
        _repository = repository;
        _notifications = notifications;
    }

    /// <summary>
    /// Lab receives sample → SampleStatus.Received (or Rejected). Appends status_history.
    /// </summary>
    public async Task<SampleDto> CheckInAsync(
        CheckInSampleRequest request, string technicianId, CancellationToken ct = default)
    {
        var sample = await _repository.FindByCodeAsync(request.Code.Trim(), ct)
            ?? throw new InvalidOperationException("MSG-ERR-SAMPLE-01: Không tìm thấy mẫu (barcode/mã SMP).");

        if (sample.Status == SampleStatus.Rejected)
            throw new InvalidOperationException("MSG-ERR-SAMPLE-02: Mẫu đã bị từ chối trước đó.");

        if (sample.Status is SampleStatus.Received or SampleStatus.Processing or SampleStatus.Testing or SampleStatus.Completed)
            throw new InvalidOperationException($"MSG-ERR-SAMPLE-03: Mẫu đã check-in (status={sample.Status}).");

        if (!AllowedInbound.Contains(sample.Status))
            throw new InvalidOperationException($"MSG-ERR-SAMPLE-04: Trạng thái {sample.Status} không thể check-in.");

        var from = sample.StatusString;

        if (!string.IsNullOrWhiteSpace(request.RejectionCode))
        {
            sample.Status = SampleStatus.Rejected;
            sample.RejectionReason = $"{request.RejectionCode}: {request.RejectionNote}".Trim();
            sample.RejectedAt = DateTime.UtcNow;
        }
        else
        {
            sample.Status = SampleStatus.Received;
            sample.ReceivedAt = DateTime.UtcNow;
            sample.ReceivedBy = technicianId;
            sample.LabRoom = request.LabRoomId;
            sample.RejectionReason = null;
        }

        sample.StatusHistory.Add(new SampleStatusHistoryEntry
        {
            FromStatus = from,
            ToStatus = sample.StatusString,
            ChangedAt = DateTime.UtcNow,
            ChangedBy = technicianId,
            Notes = request.RejectionNote
        });
        sample.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(sample, ct);

        if (_notifications != null)
        {
            try
            {
                if (sample.Status == SampleStatus.Rejected)
                {
                    if (!string.IsNullOrWhiteSpace(sample.PatientId))
                    {
                        await _notifications.CreateNotificationAsync(new CreateNotificationDto
                        {
                            UserId = sample.PatientId,
                            Role = RoleNames.Patient,
                            Title = $"Mẫu {sample.Barcode} cần lấy lại",
                            Message = $"Mẫu bệnh phẩm {sample.Barcode} bị từ chối tại phòng Lab ({sample.RejectionReason}). Vui lòng liên hệ nhân viên y tế.",
                            Type = "SampleRejected",
                            ReferenceId = sample.Id
                        }, ct);
                    }
                    await _notifications.CreateNotificationAsync(new CreateNotificationDto
                    {
                        UserId = "ROLE_DOCTOR",
                        Role = RoleNames.Doctor,
                        Title = $"Mẫu {sample.Barcode} bị từ chối",
                        Message = $"Phòng Lab đã từ chối mẫu {sample.Barcode} của BN {sample.PatientName}: {sample.RejectionReason}.",
                        Type = "SampleRejected",
                        ReferenceId = sample.Id
                    }, ct);
                }
                else if (!string.IsNullOrWhiteSpace(sample.PatientId))
                {
                    await _notifications.CreateNotificationAsync(new CreateNotificationDto
                    {
                        UserId = sample.PatientId,
                        Role = RoleNames.Patient,
                        Title = $"Phòng Lab đã tiếp nhận mẫu {sample.Barcode}",
                        Message = $"Mẫu xét nghiệm {sample.Barcode} của bạn đã được tiếp nhận và chuẩn bị đưa vào máy phân tích.",
                        Type = "SampleReceived",
                        ReferenceId = sample.Id
                    }, ct);
                }
            }
            catch { /* non-blocking */ }
        }

        return Map(sample);
    }

    public async Task<SampleDto?> LookupAsync(string code, CancellationToken ct = default)
    {
        var s = await _repository.FindByCodeAsync(code.Trim(), ct);
        return s == null ? null : Map(s);
    }

    public async Task<List<SampleDto>> ListPendingAsync(CancellationToken ct = default)
    {
        var list = await _repository.ListPendingCheckInAsync(ct);
        return list.Select(Map).ToList();
    }

    private static SampleDto Map(Sample s) => new()
    {
        SampleId = s.Id,
        Barcode = s.Barcode,
        PatientId = s.PatientId,
        Status = s.StatusString,
        TubeColor = s.TubeColor,
        Anticoagulant = s.Anticoagulant,
        VolumeMl = s.VolumeMl,
        CollectedAt = s.CollectedAt,
        CheckedInAt = s.ReceivedAt,
        LabRoomId = s.LabRoom,
        RejectionReason = s.RejectionReason
    };
}
