using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository interface for IoT environmental monitoring thresholds and alert recipients (UC67).
/// </summary>
public interface IIotThresholdRepository
{
    Task<IEnumerable<IotDeviceThreshold>> GetAllThresholdsAsync();
    Task<IotDeviceThreshold?> GetThresholdByDeviceIdAsync(string deviceId);
    Task<bool> UpsertThresholdAsync(IotDeviceThreshold threshold);
    Task<bool> BatchUpsertThresholdsAsync(IEnumerable<IotDeviceThreshold> thresholds);
    Task<IEnumerable<IotAlertRecipient>> GetAllRecipientsAsync();
    Task<IotAlertRecipient> AddRecipientAsync(IotAlertRecipient recipient);
    Task<bool> RemoveRecipientAsync(string recipientId);
    Task<bool> ResetDefaultsAsync();
}
