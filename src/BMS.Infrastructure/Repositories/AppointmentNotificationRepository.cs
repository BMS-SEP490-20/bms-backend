using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB repository implementation for appointment notifications (UC22).
/// Includes in-memory synchronization with BmsDbContext for development resiliency.
/// </summary>
public class AppointmentNotificationRepository : IAppointmentNotificationRepository
{
    private readonly IMongoCollection<AppointmentNotification>? _collection;
    private readonly BmsDbContext _dbContext;

    public AppointmentNotificationRepository(BmsDbContext dbContext, IMongoDatabase? database = null)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _collection = database.GetCollection<AppointmentNotification>("appointment_notifications");
                var indexKeys = Builders<AppointmentNotification>.IndexKeys
                    .Ascending(x => x.AppointmentId)
                    .Ascending(x => x.PatientId);
                _collection.Indexes.CreateOne(new CreateIndexModel<AppointmentNotification>(indexKeys));
            }
            catch
            {
                _collection = null;
            }
        }
    }

    public async Task<List<AppointmentNotification>> GetByAppointmentIdAsync(string appointmentId, CancellationToken ct = default)
    {
        if (_collection != null)
        {
            try
            {
                var list = await _collection.Find(x => x.AppointmentId == appointmentId || x.AppointmentCode == appointmentId)
                    .SortByDescending(x => x.SentAt)
                    .ToListAsync(ct);
                if (list.Count > 0) return list;
            }
            catch { }
        }

        lock (_dbContext.AppointmentNotifications)
        {
            return _dbContext.AppointmentNotifications
                .Where(x => x.AppointmentId == appointmentId || x.AppointmentCode == appointmentId)
                .OrderByDescending(x => x.SentAt)
                .ToList();
        }
    }

    public async Task<List<AppointmentNotification>> GetByPatientIdAsync(string patientId, CancellationToken ct = default)
    {
        if (_collection != null)
        {
            try
            {
                var list = await _collection.Find(x => x.PatientId == patientId)
                    .SortByDescending(x => x.SentAt)
                    .ToListAsync(ct);
                if (list.Count > 0) return list;
            }
            catch { }
        }

        lock (_dbContext.AppointmentNotifications)
        {
            return _dbContext.AppointmentNotifications
                .Where(x => x.PatientId == patientId)
                .OrderByDescending(x => x.SentAt)
                .ToList();
        }
    }

    public async Task<AppointmentNotification?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (_collection != null)
        {
            try
            {
                var item = await _collection.Find(x => x.Id == id).FirstOrDefaultAsync(ct);
                if (item != null) return item;
            }
            catch { }
        }

        lock (_dbContext.AppointmentNotifications)
        {
            return _dbContext.AppointmentNotifications.FirstOrDefault(x => x.Id == id);
        }
    }

    public async Task InsertAsync(AppointmentNotification notification, CancellationToken ct = default)
    {
        lock (_dbContext.AppointmentNotifications)
        {
            _dbContext.AppointmentNotifications.Add(notification);
        }

        if (_collection != null)
        {
            try
            {
                await _collection.InsertOneAsync(notification, cancellationToken: ct);
            }
            catch { }
        }
    }

    public async Task<bool> MarkAsReadAsync(string notificationId, CancellationToken ct = default)
    {
        bool updated = false;
        lock (_dbContext.AppointmentNotifications)
        {
            var item = _dbContext.AppointmentNotifications.FirstOrDefault(x => x.Id == notificationId);
            if (item != null)
            {
                item.IsRead = true;
                updated = true;
            }
        }

        if (_collection != null)
        {
            try
            {
                var update = Builders<AppointmentNotification>.Update.Set(x => x.IsRead, true);
                var result = await _collection.UpdateOneAsync(x => x.Id == notificationId, update, cancellationToken: ct);
                if (result.ModifiedCount > 0) updated = true;
            }
            catch { }
        }

        return updated;
    }
}
