using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for Specimen History retrieval, tracking event logging and storage inspection (UC70).
/// </summary>
public interface ISpecimenHistoryService
{
    Task<PagedResult<SpecimenHistoryListItemDto>> GetSpecimenHistoryPagedAsync(
        SpecimenHistoryQueryParameters query,
        CancellationToken ct = default);

    Task<SpecimenHistoryDetailDto?> GetSpecimenHistoryByBarcodeAsync(
        string barcode,
        CancellationToken ct = default);

    Task<List<SpecimenHistoryListItemDto>> GetSpecimensByPatientIdAsync(
        string patientId,
        CancellationToken ct = default);

    Task<(bool Success, string Message, SpecimenHistoryDetailDto? Data)> AddTrackingEventAsync(
        string barcode,
        AddSpecimenTrackingEventDto eventDto,
        string operatorId,
        string operatorName,
        CancellationToken ct = default);

    Task<(bool Success, string Message, SpecimenHistoryDetailDto? Data)> UpdateStorageAsync(
        string barcode,
        UpdateSpecimenStorageDto storageDto,
        string operatorName,
        CancellationToken ct = default);
}
