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
/// Repository implementation for clinical laboratory diagnostic reports (UC42).
/// Integrates MongoDB collection 'test_reports' with in-memory synchronization.
/// </summary>
public class TestReportRepository : ITestReportRepository
{
    private readonly IMongoCollection<TestReport>? _collection;
    private readonly BmsDbContext _dbContext;

    public TestReportRepository(IMongoDatabase? database, BmsDbContext dbContext)
    {
        _dbContext = dbContext;
        if (database != null)
        {
            try
            {
                _collection = database.GetCollection<TestReport>("test_reports");
            }
            catch
            {
                _collection = null;
            }
        }
    }

    public async Task<TestReport?> GetByIdAsync(string id)
    {
        if (_collection != null)
        {
            try
            {
                var doc = await _collection.Find(r => r.Id == id).FirstOrDefaultAsync();
                if (doc != null) return doc;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestReports)
        {
            return _dbContext.TestReports.FirstOrDefault(r => r.Id == id);
        }
    }

    public async Task<TestReport?> GetByReportIdAsync(string reportId)
    {
        if (_collection != null)
        {
            try
            {
                var doc = await _collection.Find(r => r.ReportId == reportId).FirstOrDefaultAsync();
                if (doc != null) return doc;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestReports)
        {
            return _dbContext.TestReports.FirstOrDefault(r => r.ReportId.Equals(reportId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<TestReport?> GetByOrderIdAsync(string orderId)
    {
        if (_collection != null)
        {
            try
            {
                var doc = await _collection.Find(r => r.OrderId == orderId).FirstOrDefaultAsync();
                if (doc != null) return doc;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestReports)
        {
            return _dbContext.TestReports.FirstOrDefault(r => r.OrderId.Equals(orderId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<IEnumerable<TestReport>> GetByPatientIdAsync(string patientId)
    {
        if (_collection != null)
        {
            try
            {
                var list = await _collection.Find(r => r.PatientId == patientId).SortByDescending(r => r.IssuedAt).ToListAsync();
                if (list != null && list.Count > 0) return list;
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestReports)
        {
            return _dbContext.TestReports
                .Where(r => r.PatientId.Equals(patientId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(r => r.IssuedAt)
                .ToList();
        }
    }

    public async Task<TestReport> CreateAsync(TestReport report)
    {
        if (string.IsNullOrEmpty(report.ReportId))
        {
            report.ReportId = await GenerateNextReportIdAsync();
        }

        report.CreatedAt = DateTime.UtcNow;

        if (_collection != null)
        {
            try
            {
                await _collection.InsertOneAsync(report);
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestReports)
        {
            _dbContext.TestReports.Add(report);
        }

        return report;
    }

    public async Task<bool> UpdateAsync(TestReport report)
    {
        if (_collection != null)
        {
            try
            {
                var result = await _collection.ReplaceOneAsync(r => r.Id == report.Id, report);
                if (result.IsAcknowledged && result.ModifiedCount > 0)
                {
                    lock (_dbContext.TestReports)
                    {
                        var idx = _dbContext.TestReports.FindIndex(r => r.Id == report.Id);
                        if (idx >= 0) _dbContext.TestReports[idx] = report;
                    }
                    return true;
                }
            }
            catch
            {
                // Fall back to in-memory store
            }
        }

        lock (_dbContext.TestReports)
        {
            var idx = _dbContext.TestReports.FindIndex(r => r.Id == report.Id);
            if (idx >= 0)
            {
                _dbContext.TestReports[idx] = report;
                return true;
            }
        }

        return false;
    }

    public Task<string> GenerateNextReportIdAsync()
    {
        var datePrefix = DateTime.UtcNow.ToString("yyMMdd");
        int count;
        lock (_dbContext.TestReports)
        {
            count = _dbContext.TestReports.Count(r => r.ReportId.StartsWith($"RPT-{datePrefix}")) + 1;
        }

        return Task.FromResult($"RPT-{datePrefix}-{count:D4}");
    }
}
