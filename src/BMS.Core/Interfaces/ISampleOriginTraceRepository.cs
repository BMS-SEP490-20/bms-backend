using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Repository contract for specimen origin traceability and chain of custody audit (UC77).
/// </summary>
public interface ISampleOriginTraceRepository
{
    Task<SampleOriginTrace?> GetBySpecimenIdAsync(string specimenId);
    Task<IEnumerable<SampleOriginTrace>> GetRecentTracesAsync(int limit = 10);
    Task<bool> UpsertTraceAsync(SampleOriginTrace trace);
}
