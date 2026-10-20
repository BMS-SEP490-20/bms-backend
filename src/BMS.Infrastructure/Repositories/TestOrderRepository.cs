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
/// Repository implementation for laboratory test orders (UC32).
/// Supports MongoDB collection 'test_orders' with in-memory synchronization.
/// </summary>
public class TestOrderRepository : ITestOrderRepository
{
    private readonly IMongoCollection<TestOrder>? _collection;
    private readonly BmsDbContext _dbContext;

    public TestOrderRepository(IMongoDatabase? database, BmsDbContext dbContext)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _collection = database.GetCollection<TestOrder>("test_orders");
            }
            catch
            {
                _collection = null;
            }
        }
    }

    public async Task<TestOrder?> GetByIdAsync(string id)
    {
        if (_collection != null)
        {
            try
            {
                var doc = await _collection.Find(o => o.Id == id).FirstOrDefaultAsync();
                if (doc != null) return doc;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestOrders)
        {
            return _dbContext.TestOrders.FirstOrDefault(o => o.Id == id);
        }
    }

    public async Task<TestOrder?> GetByOrderNumberAsync(string orderNumber)
    {
        if (_collection != null)
        {
            try
            {
                var doc = await _collection.Find(o => o.OrderNumber == orderNumber).FirstOrDefaultAsync();
                if (doc != null) return doc;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestOrders)
        {
            return _dbContext.TestOrders.FirstOrDefault(o => o.OrderNumber.Equals(orderNumber, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<IEnumerable<TestOrder>> GetAllAsync(string? patientId = null, string? doctorId = null, TestOrderStatus? status = null)
    {
        if (_collection != null)
        {
            try
            {
                var builder = Builders<TestOrder>.Filter;
                var filter = builder.Empty;

                if (!string.IsNullOrEmpty(patientId))
                {
                    filter &= builder.Eq(o => o.PatientId, patientId);
                }

                if (!string.IsNullOrEmpty(doctorId))
                {
                    filter &= builder.Eq(o => o.DoctorId, doctorId);
                }

                if (status.HasValue)
                {
                    filter &= builder.Eq(o => o.Status, status.Value);
                }

                var list = await _collection.Find(filter).SortByDescending(o => o.CreatedAt).ToListAsync();
                if (list != null && list.Count > 0) return list;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestOrders)
        {
            var query = _dbContext.TestOrders.AsQueryable();

            if (!string.IsNullOrEmpty(patientId))
            {
                query = query.Where(o => o.PatientId == patientId);
            }

            if (!string.IsNullOrEmpty(doctorId))
            {
                query = query.Where(o => o.DoctorId == doctorId);
            }

            if (status.HasValue)
            {
                query = query.Where(o => o.Status == status.Value);
            }

            return query.OrderByDescending(o => o.CreatedAt).ToList();
        }
    }

    public async Task<TestOrder> CreateAsync(TestOrder order)
    {
        if (string.IsNullOrEmpty(order.OrderNumber))
        {
            order.OrderNumber = await GenerateNextOrderNumberAsync();
        }

        order.CreatedAt = DateTime.UtcNow;

        if (_collection != null)
        {
            try
            {
                await _collection.InsertOneAsync(order);
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestOrders)
        {
            _dbContext.TestOrders.Add(order);
        }

        return order;
    }

    public async Task<bool> UpdateAsync(TestOrder order)
    {
        order.UpdatedAt = DateTime.UtcNow;

        if (_collection != null)
        {
            try
            {
                var result = await _collection.ReplaceOneAsync(o => o.Id == order.Id, order);
                if (result.IsAcknowledged && result.ModifiedCount > 0)
                {
                    // Update in-memory copy
                    lock (_dbContext.TestOrders)
                    {
                        var idx = _dbContext.TestOrders.FindIndex(o => o.Id == order.Id);
                        if (idx >= 0) _dbContext.TestOrders[idx] = order;
                    }
                    return true;
                }
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestOrders)
        {
            var idx = _dbContext.TestOrders.FindIndex(o => o.Id == order.Id);
            if (idx >= 0)
            {
                _dbContext.TestOrders[idx] = order;
                return true;
            }
        }

        return false;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        if (_collection != null)
        {
            try
            {
                var result = await _collection.DeleteOneAsync(o => o.Id == id);
                if (result.IsAcknowledged && result.DeletedCount > 0)
                {
                    lock (_dbContext.TestOrders)
                    {
                        _dbContext.TestOrders.RemoveAll(o => o.Id == id);
                    }
                    return true;
                }
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestOrders)
        {
            return _dbContext.TestOrders.RemoveAll(o => o.Id == id) > 0;
        }
    }

    public Task<string> GenerateNextOrderNumberAsync()
    {
        var datePrefix = DateTime.UtcNow.ToString("yyyyMMdd");
        int count;
        lock (_dbContext.TestOrders)
        {
            count = _dbContext.TestOrders.Count(o => o.OrderNumber.StartsWith($"ORD-{datePrefix}")) + 1;
        }

        return Task.FromResult($"ORD-{datePrefix}-{count:D4}");
    }
}
