using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Data access contract for clinical laboratory diagnostic reports (UC42).
/// </summary>
public interface ITestReportRepository
{
    Task<TestReport?> GetByIdAsync(string id);
    Task<TestReport?> GetByReportIdAsync(string reportId);
    Task<TestReport?> GetByOrderIdAsync(string orderId);
    Task<IEnumerable<TestReport>> GetByPatientIdAsync(string patientId);
    Task<TestReport> CreateAsync(TestReport report);
    Task<bool> UpdateAsync(TestReport report);
    Task<string> GenerateNextReportIdAsync();
}
