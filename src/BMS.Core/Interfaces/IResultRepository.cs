using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

public interface IResultRepository
{
    Task<Sample?> GetSampleAsync(string sampleId, CancellationToken ct = default);
    Task InsertAsync(TestResult result, CancellationToken ct = default);
    Task<string> GenerateNextResultCodeAsync(DateTime date, CancellationToken ct = default);
    Task UpdateSampleStatusAsync(string sampleId, string status, CancellationToken ct = default);
}
