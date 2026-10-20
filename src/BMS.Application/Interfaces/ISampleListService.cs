using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract for managing phlebotomy specimen intake and collection lists (UC37).
/// </summary>
public interface ISampleListService
{
    Task<IEnumerable<CheckedInAppointmentDto>> GetCheckedInAppointmentsAsync();
    Task<IEnumerable<SampleListResponseDto>> GetCollectionBatchesAsync(DateTime? date = null);
    Task<SampleListResponseDto?> GetBatchByCodeAsync(string batchCode);
    Task<SampleListResponseDto> CreateCollectionListAsync(CreateSampleListRequestDto request, string? currentUserId = null);
    Task<BatchPrintBarcodesResponseDto?> GenerateBatchBarcodesAsync(string batchCode);
}
