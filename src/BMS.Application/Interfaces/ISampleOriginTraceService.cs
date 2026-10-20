using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs.Laboratory;

namespace BMS.Application.Interfaces;

/// <summary>
/// Service contract for specimen origin traceability and chain of custody audit (UC77 / Issue I3-088).
/// </summary>
public interface ISampleOriginTraceService
{
    Task<SampleOriginTraceDto?> GetOriginTraceBySpecimenIdAsync(string specimenId);
    Task<IEnumerable<RecentTraceableSpecimenDto>> GetRecentTraceableSpecimensAsync();
    Task<ExportTracePdfResponseDto?> ExportTracePdfAsync(string specimenId);
}
