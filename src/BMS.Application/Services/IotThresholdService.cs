using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BMS.Application.DTOs.IoT;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>
/// Service implementing IoT sensor telemetry threshold configuration and alarm management (UC67 / Configure IoT Thresholds).
/// </summary>
public class IotThresholdService : IIotThresholdService
{
    private readonly IIotThresholdRepository _repository;

    public IotThresholdService(IIotThresholdRepository repository)
    {
        _repository = repository;
    }

    public async Task<IotThresholdConfigSummaryDto> GetConfigSummaryAsync()
    {
        var thresholds = await GetAllThresholdsAsync();
        var recipients = await GetAllRecipientsAsync();

        return new IotThresholdConfigSummaryDto
        {
            TotalDevices = thresholds.Count(),
            TotalRules = thresholds.Count(),
            Thresholds = thresholds.ToList(),
            Recipients = recipients.ToList(),
            EscalationPolicy = "Alert channels: Push notification (FCM) + Email. SMS escalation after 5 min if not acknowledged."
        };
    }

    public async Task<IEnumerable<IotThresholdDto>> GetAllThresholdsAsync()
    {
        var entities = await _repository.GetAllThresholdsAsync();
        return entities.Select(MapThresholdToDto);
    }

    public async Task<IotThresholdDto?> GetThresholdByDeviceIdAsync(string deviceId)
    {
        var entity = await _repository.GetThresholdByDeviceIdAsync(deviceId);
        return entity != null ? MapThresholdToDto(entity) : null;
    }

    public async Task<IotThresholdDto?> UpdateThresholdAsync(string deviceId, UpdateIotThresholdRequestDto dto, string? updatedBy)
    {
        var entity = await _repository.GetThresholdByDeviceIdAsync(deviceId);
        if (entity == null) return null;

        entity.LowAlert = dto.LowAlert;
        entity.HighAlert = dto.HighAlert;
        entity.IntervalSeconds = dto.IntervalSeconds;
        if (!string.IsNullOrWhiteSpace(dto.Status)) entity.Status = dto.Status;
        entity.LastUpdated = DateTime.UtcNow;
        entity.UpdatedBy = updatedBy ?? "Manager";

        await _repository.UpsertThresholdAsync(entity);
        return MapThresholdToDto(entity);
    }

    public async Task<bool> BatchUpdateThresholdsAsync(BatchUpdateIotThresholdsRequestDto dto, string? updatedBy)
    {
        if (dto.Devices == null || !dto.Devices.Any()) return false;

        foreach (var item in dto.Devices)
        {
            var entity = await _repository.GetThresholdByDeviceIdAsync(item.DeviceId);
            if (entity != null)
            {
                entity.LowAlert = item.LowAlert;
                entity.HighAlert = item.HighAlert;
                entity.IntervalSeconds = item.IntervalSeconds;
                entity.LastUpdated = DateTime.UtcNow;
                entity.UpdatedBy = updatedBy ?? "Manager";
                await _repository.UpsertThresholdAsync(entity);
            }
        }

        return true;
    }

    public async Task<bool> ResetDefaultsAsync(string? updatedBy)
    {
        return await _repository.ResetDefaultsAsync();
    }

    public async Task<IEnumerable<IotAlertRecipientDto>> GetAllRecipientsAsync()
    {
        var entities = await _repository.GetAllRecipientsAsync();
        return entities.Select(MapRecipientToDto);
    }

    public async Task<IotAlertRecipientDto> AddRecipientAsync(AddIotAlertRecipientRequestDto dto)
    {
        var entity = new IotAlertRecipient
        {
            Id = Guid.NewGuid().ToString(),
            FullName = dto.FullName,
            Role = dto.Role,
            Email = dto.Email,
            Phone = dto.Phone,
            Channels = dto.Channels ?? new List<string> { "Push", "Email" },
            IsActive = true
        };

        var created = await _repository.AddRecipientAsync(entity);
        return MapRecipientToDto(created);
    }

    public async Task<bool> RemoveRecipientAsync(string recipientId)
    {
        return await _repository.RemoveRecipientAsync(recipientId);
    }

    private static IotThresholdDto MapThresholdToDto(IotDeviceThreshold entity)
    {
        return new IotThresholdDto
        {
            Id = entity.Id,
            DeviceId = entity.DeviceId,
            DeviceName = entity.DeviceName,
            Location = entity.Location,
            SensorType = entity.SensorType,
            Unit = entity.Unit,
            LowAlert = entity.LowAlert,
            HighAlert = entity.HighAlert,
            IntervalSeconds = entity.IntervalSeconds,
            Status = entity.Status,
            LastUpdated = entity.LastUpdated,
            UpdatedBy = entity.UpdatedBy
        };
    }

    private static IotAlertRecipientDto MapRecipientToDto(IotAlertRecipient entity)
    {
        return new IotAlertRecipientDto
        {
            Id = entity.Id,
            FullName = entity.FullName,
            Role = entity.Role,
            Email = entity.Email,
            Phone = entity.Phone,
            Channels = entity.Channels,
            IsActive = entity.IsActive
        };
    }
}
