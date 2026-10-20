using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB & BmsDbContext implementation of IIotThresholdRepository (UC67 / Configure IoT Thresholds).
/// </summary>
public class IotThresholdRepository : IIotThresholdRepository
{
    private readonly IMongoCollection<IotDeviceThreshold>? _thresholdsCollection;
    private readonly IMongoCollection<IotAlertRecipient>? _recipientsCollection;
    private readonly BmsDbContext _dbContext;

    public IotThresholdRepository(IMongoDatabase? database, BmsDbContext dbContext)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _thresholdsCollection = database.GetCollection<IotDeviceThreshold>("iot_thresholds");
                _recipientsCollection = database.GetCollection<IotAlertRecipient>("iot_recipients");
            }
            catch
            {
                // In-memory fallback
            }
        }
    }

    public async Task<IEnumerable<IotDeviceThreshold>> GetAllThresholdsAsync()
    {
        if (_thresholdsCollection != null)
        {
            try
            {
                var list = await _thresholdsCollection.Find(_ => true).ToListAsync();
                if (list != null && list.Any())
                {
                    lock (_dbContext.IotThresholds)
                    {
                        foreach (var item in list)
                        {
                            var idx = _dbContext.IotThresholds.FindIndex(t => t.DeviceId == item.DeviceId);
                            if (idx >= 0) _dbContext.IotThresholds[idx] = item;
                            else _dbContext.IotThresholds.Add(item);
                        }
                    }
                    return list;
                }
            }
            catch
            {
                // Fallback to in-memory store
            }
        }

        lock (_dbContext.IotThresholds)
        {
            return _dbContext.IotThresholds.ToList();
        }
    }

    public async Task<IotDeviceThreshold?> GetThresholdByDeviceIdAsync(string deviceId)
    {
        if (_thresholdsCollection != null)
        {
            try
            {
                var item = await _thresholdsCollection
                    .Find(t => t.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefaultAsync();
                if (item != null) return item;
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.IotThresholds)
        {
            return _dbContext.IotThresholds.FirstOrDefault(t =>
                t.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<bool> UpsertThresholdAsync(IotDeviceThreshold threshold)
    {
        threshold.LastUpdated = DateTime.UtcNow;

        if (_thresholdsCollection != null)
        {
            try
            {
                await _thresholdsCollection.ReplaceOneAsync(
                    t => t.DeviceId == threshold.DeviceId,
                    threshold,
                    new ReplaceOptions { IsUpsert = true });
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.IotThresholds)
        {
            var idx = _dbContext.IotThresholds.FindIndex(t => t.DeviceId == threshold.DeviceId);
            if (idx >= 0) _dbContext.IotThresholds[idx] = threshold;
            else _dbContext.IotThresholds.Add(threshold);
        }

        return await Task.FromResult(true);
    }

    public async Task<bool> BatchUpsertThresholdsAsync(IEnumerable<IotDeviceThreshold> thresholds)
    {
        foreach (var t in thresholds)
        {
            await UpsertThresholdAsync(t);
        }
        return true;
    }

    public async Task<IEnumerable<IotAlertRecipient>> GetAllRecipientsAsync()
    {
        if (_recipientsCollection != null)
        {
            try
            {
                var list = await _recipientsCollection.Find(_ => true).ToListAsync();
                if (list != null && list.Any())
                {
                    lock (_dbContext.IotAlertRecipients)
                    {
                        foreach (var item in list)
                        {
                            var idx = _dbContext.IotAlertRecipients.FindIndex(r => r.Id == item.Id);
                            if (idx >= 0) _dbContext.IotAlertRecipients[idx] = item;
                            else _dbContext.IotAlertRecipients.Add(item);
                        }
                    }
                    return list;
                }
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.IotAlertRecipients)
        {
            return _dbContext.IotAlertRecipients.ToList();
        }
    }

    public async Task<IotAlertRecipient> AddRecipientAsync(IotAlertRecipient recipient)
    {
        if (_recipientsCollection != null)
        {
            try
            {
                await _recipientsCollection.InsertOneAsync(recipient);
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.IotAlertRecipients)
        {
            _dbContext.IotAlertRecipients.Add(recipient);
        }

        return await Task.FromResult(recipient);
    }

    public async Task<bool> RemoveRecipientAsync(string recipientId)
    {
        if (_recipientsCollection != null)
        {
            try
            {
                await _recipientsCollection.DeleteOneAsync(r => r.Id == recipientId);
            }
            catch
            {
                // Fallback
            }
        }

        lock (_dbContext.IotAlertRecipients)
        {
            var idx = _dbContext.IotAlertRecipients.FindIndex(r => r.Id == recipientId);
            if (idx >= 0)
            {
                _dbContext.IotAlertRecipients.RemoveAt(idx);
                return await Task.FromResult(true);
            }
        }

        return await Task.FromResult(false);
    }

    public async Task<bool> ResetDefaultsAsync()
    {
        var defaultThresholds = new[]
        {
            new IotDeviceThreshold { DeviceId = "TEMP-001", DeviceName = "Fridge Temperature Sensor 1", Location = "Fridge R-01", SensorType = "Temperature", Unit = "°C", LowAlert = 2.0, HighAlert = 8.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "TEMP-002", DeviceName = "Fridge Temperature Sensor 2", Location = "Fridge R-02", SensorType = "Temperature", Unit = "°C", LowAlert = 2.0, HighAlert = 8.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "TEMP-003", DeviceName = "Freezer Temperature Sensor", Location = "Freezer F-01", SensorType = "Temperature", Unit = "°C", LowAlert = -25.0, HighAlert = -15.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "TEMP-004", DeviceName = "Ambient Lab Temperature", Location = "Lab Room 01", SensorType = "Temperature", Unit = "°C", LowAlert = 18.0, HighAlert = 26.0, IntervalSeconds = 120, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "HUM-001", DeviceName = "Cold Storage Humidity", Location = "Cold Storage", SensorType = "Humidity", Unit = "%", LowAlert = 30.0, HighAlert = 60.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "HUM-002", DeviceName = "Lab Room Humidity", Location = "Lab Room 01", SensorType = "Humidity", Unit = "%", LowAlert = 30.0, HighAlert = 60.0, IntervalSeconds = 120, Status = "Active" }
        };

        await BatchUpsertThresholdsAsync(defaultThresholds);
        return true;
    }
}
