using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service interface for configuring IoT telemetry thresholds, alarms, and alert escalation (UC67).
/// </summary>
public interface IIotThresholdService
{
    Task<IotThresholdConfigSummaryDto> GetConfigSummaryAsync();
    Task<IEnumerable<IotThresholdDto>> GetAllThresholdsAsync();
    Task<IotThresholdDto?> GetThresholdByDeviceIdAsync(string deviceId);
    Task<IotThresholdDto?> UpdateThresholdAsync(string deviceId, UpdateIotThresholdRequestDto dto, string? updatedBy);
    Task<bool> BatchUpdateThresholdsAsync(BatchUpdateIotThresholdsRequestDto dto, string? updatedBy);
    Task<bool> ResetDefaultsAsync(string? updatedBy);
    Task<IEnumerable<IotAlertRecipientDto>> GetAllRecipientsAsync();
    Task<IotAlertRecipientDto> AddRecipientAsync(AddIotAlertRecipientRequestDto dto);
    Task<bool> RemoveRecipientAsync(string recipientId);
}
