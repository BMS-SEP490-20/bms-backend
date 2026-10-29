using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>
/// Clinical application service implementing Specimen History retrieval and audit tracking (UC70).
/// Adheres to: ISO 15189:2022, Decision 2429/QD-BYT and BR-PHL-01 to BR-PHL-04.
/// </summary>
public class SpecimenHistoryService : ISpecimenHistoryService
{
    private readonly ISpecimenHistoryRepository _repository;

    public SpecimenHistoryService(ISpecimenHistoryRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<SpecimenHistoryListItemDto>> GetSpecimenHistoryPagedAsync(
        SpecimenHistoryQueryParameters query,
        CancellationToken ct = default)
    {
        var (items, total) = await _repository.GetPagedAsync(
            query.Page,
            query.PageSize,
            query.Keyword,
            query.SampleType,
            query.Status,
            query.FromDate,
            query.ToDate,
            ct);

        return new PagedResult<SpecimenHistoryListItemDto>
        {
            CurrentPage = query.Page,
            PageSize = query.PageSize,
            TotalCount = total,
            Items = items.Select(MapToListItemDto).ToList()
        };
    }

    public async Task<SpecimenHistoryDetailDto?> GetSpecimenHistoryByBarcodeAsync(
        string barcode,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetByBarcodeAsync(barcode, ct);
        return entity == null ? null : MapToDetailDto(entity);
    }

    public async Task<List<SpecimenHistoryListItemDto>> GetSpecimensByPatientIdAsync(
        string patientId,
        CancellationToken ct = default)
    {
        var items = await _repository.GetByPatientIdAsync(patientId, ct);
        return items.Select(MapToListItemDto).ToList();
    }

    public async Task<(bool Success, string Message, SpecimenHistoryDetailDto? Data)> AddTrackingEventAsync(
        string barcode,
        AddSpecimenTrackingEventDto eventDto,
        string operatorId,
        string operatorName,
        CancellationToken ct = default)
    {
        var existing = await _repository.GetByBarcodeAsync(barcode, ct);
        if (existing == null)
        {
            return (false, $"Không tìm thấy mẫu bệnh phẩm với mã '{barcode}'.", null);
        }

        var newEvent = new SpecimenTimelineEvent
        {
            EventId = Guid.NewGuid().ToString("N"),
            EventType = eventDto.EventType,
            Title = eventDto.Title.Trim(),
            Description = eventDto.Description?.Trim() ?? string.Empty,
            Timestamp = DateTime.UtcNow,
            OperatorId = operatorId,
            OperatorName = !string.IsNullOrWhiteSpace(eventDto.OperatorName) ? eventDto.OperatorName.Trim() : operatorName,
            OperatorRole = eventDto.OperatorRole?.Trim() ?? BMS.Core.Constants.RoleNames.Lab,
            Location = eventDto.Location?.Trim() ?? "Khu Vực Xét Nghiệm",
            Status = !string.IsNullOrWhiteSpace(eventDto.Status) ? eventDto.Status.Trim() : existing.Status,
            TemperatureC = eventDto.TemperatureC,
            HumidityPercent = eventDto.HumidityPercent,
            Notes = eventDto.Notes?.Trim()
        };

        var added = await _repository.AddTimelineEventAsync(barcode, newEvent, ct);
        if (!added)
        {
            return (false, "Lỗi khi ghi nhận sự kiện theo dõi mẫu.", null);
        }

        var updated = await _repository.GetByBarcodeAsync(barcode, ct);
        return (true, "Đã ghi nhận sự kiện theo dõi mẫu thành công.", MapToDetailDto(updated!));
    }

    public async Task<(bool Success, string Message, SpecimenHistoryDetailDto? Data)> UpdateStorageAsync(
        string barcode,
        UpdateSpecimenStorageDto storageDto,
        string operatorName,
        CancellationToken ct = default)
    {
        var existing = await _repository.GetByBarcodeAsync(barcode, ct);
        if (existing == null)
        {
            return (false, $"Không tìm thấy mẫu bệnh phẩm với mã '{barcode}'.", null);
        }

        var retentionDays = storageDto.RetentionPeriodDays > 0 ? storageDto.RetentionPeriodDays : 7;
        var expDate = DateTime.UtcNow.AddDays(retentionDays);

        var isCompliant = storageDto.CurrentTemperature >= storageDto.TargetTemperatureMin &&
                          storageDto.CurrentTemperature <= storageDto.TargetTemperatureMax;

        var storageInfo = new SpecimenStorageInfo
        {
            StorageStatus = !string.IsNullOrWhiteSpace(storageDto.StorageStatus) ? storageDto.StorageStatus.Trim() : "Stored",
            StorageUnit = storageDto.StorageUnit.Trim(),
            RackId = storageDto.RackId.Trim(),
            SlotPosition = storageDto.SlotPosition.Trim(),
            TargetTemperatureMin = storageDto.TargetTemperatureMin,
            TargetTemperatureMax = storageDto.TargetTemperatureMax,
            CurrentTemperature = storageDto.CurrentTemperature,
            CurrentHumidity = storageDto.CurrentHumidity,
            IsTemperatureCompliant = isCompliant,
            PreservationMethod = !string.IsNullOrWhiteSpace(storageDto.PreservationMethod) 
                ? storageDto.PreservationMethod.Trim() 
                : existing.Storage.PreservationMethod,
            StoredAt = DateTime.UtcNow,
            StoredBy = operatorName,
            RetentionPeriodDays = retentionDays,
            ExpirationDate = expDate,
            DisposalDueDate = expDate,
            DisposalStatus = "Retained"
        };

        var updated = await _repository.UpdateStorageAsync(barcode, storageInfo, ct);
        if (!updated)
        {
            return (false, "Không thể cập nhật thông tin vị trí lưu trữ.", null);
        }

        var record = await _repository.GetByBarcodeAsync(barcode, ct);
        return (true, "Đã cập nhật vị trí lưu trữ và thông số bảo quản mẫu thành công.", MapToDetailDto(record!));
    }

    private static SpecimenHistoryListItemDto MapToListItemDto(SpecimenHistoryRecord rec)
    {
        var now = DateTime.UtcNow;
        var remainingDays = Math.Max(0, (rec.Storage.ExpirationDate - now).Days);

        return new SpecimenHistoryListItemDto
        {
            Id = rec.Id,
            Barcode = rec.Barcode,
            RfidTag = rec.RfidTag,
            SampleType = rec.SampleType,
            TubeColor = rec.TubeColor,
            VolumeMl = rec.VolumeMl,
            PatientId = rec.PatientId,
            PatientName = rec.PatientName,
            PatientGender = rec.PatientGender,
            PatientAge = rec.PatientAge,
            OrderId = rec.OrderId,
            TestPackageName = rec.TestPackageName,
            Status = rec.Status,
            Priority = rec.Priority,
            CollectionTime = rec.CollectionTime.ToString("dd/MM/yyyy HH:mm"),
            StorageUnit = rec.Storage.StorageUnit,
            RackId = rec.Storage.RackId,
            SlotPosition = rec.Storage.SlotPosition,
            CurrentTemperature = rec.Storage.CurrentTemperature,
            RemainingDays = remainingDays
        };
    }

    private static SpecimenHistoryDetailDto MapToDetailDto(SpecimenHistoryRecord rec)
    {
        var now = DateTime.UtcNow;
        var remainingDays = Math.Max(0, (rec.Storage.ExpirationDate - now).Days);

        return new SpecimenHistoryDetailDto
        {
            Id = rec.Id,
            Barcode = rec.Barcode,
            RfidTag = rec.RfidTag,
            SampleType = rec.SampleType,
            TubeColor = rec.TubeColor,
            Anticoagulant = rec.Anticoagulant,
            VolumeMl = rec.VolumeMl,
            MinimumRequiredVolumeMl = rec.MinimumRequiredVolumeMl,
            PatientId = rec.PatientId,
            PatientName = rec.PatientName,
            PatientGender = rec.PatientGender,
            PatientAge = rec.PatientAge,
            PatientPhone = rec.PatientPhone,
            OrderId = rec.OrderId,
            AppointmentId = rec.AppointmentId,
            OrderDate = rec.OrderDate.ToString("dd/MM/yyyy HH:mm"),
            DoctorId = rec.DoctorId,
            DoctorName = rec.DoctorName,
            Department = rec.Department,
            TestPackageName = rec.TestPackageName,
            TestCriteriaCodes = rec.TestCriteriaCodes,
            Status = rec.Status,
            Priority = rec.Priority,
            CollectionTime = rec.CollectionTime.ToString("dd/MM/yyyy HH:mm"),
            CollectorId = rec.CollectorId,
            CollectorName = rec.CollectorName,
            CollectionSite = rec.CollectionSite,
            Notes = rec.Notes,
            RejectionReason = rec.RejectionReason,
            Storage = new SpecimenStorageInfoDto
            {
                StorageStatus = rec.Storage.StorageStatus,
                StorageUnit = rec.Storage.StorageUnit,
                RackId = rec.Storage.RackId,
                SlotPosition = rec.Storage.SlotPosition,
                TargetTemperatureMin = rec.Storage.TargetTemperatureMin,
                TargetTemperatureMax = rec.Storage.TargetTemperatureMax,
                CurrentTemperature = rec.Storage.CurrentTemperature,
                CurrentHumidity = rec.Storage.CurrentHumidity,
                IsTemperatureCompliant = rec.Storage.IsTemperatureCompliant,
                PreservationMethod = rec.Storage.PreservationMethod,
                StoredAt = rec.Storage.StoredAt.ToString("dd/MM/yyyy HH:mm"),
                StoredBy = rec.Storage.StoredBy,
                RetentionPeriodDays = rec.Storage.RetentionPeriodDays,
                ExpirationDate = rec.Storage.ExpirationDate.ToString("dd/MM/yyyy"),
                DisposalDueDate = rec.Storage.DisposalDueDate.ToString("dd/MM/yyyy"),
                DisposalStatus = rec.Storage.DisposalStatus,
                RemainingDays = remainingDays
            },
            Timeline = rec.Timeline
                .OrderBy(t => t.Timestamp)
                .Select(t => new SpecimenTimelineEventDto
                {
                    EventId = t.EventId,
                    EventType = t.EventType,
                    Title = t.Title,
                    Description = t.Description,
                    Timestamp = t.Timestamp.ToString("dd/MM/yyyy HH:mm:ss"),
                    OperatorId = t.OperatorId,
                    OperatorName = t.OperatorName,
                    OperatorRole = t.OperatorRole,
                    Location = t.Location,
                    Status = t.Status,
                    TemperatureC = t.TemperatureC,
                    HumidityPercent = t.HumidityPercent,
                    Notes = t.Notes
                })
                .ToList()
        };
    }
}
