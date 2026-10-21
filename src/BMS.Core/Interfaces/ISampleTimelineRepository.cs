using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>Queries existing evidence without seeding data, changing sample state or falling back to demo records.</summary>
public interface ISampleTimelineRepository
{
    Task<List<string>> OwnPatientIdsAsync(string accountId, CancellationToken ct = default);
    Task<(List<SampleTimelineSummary> Items, long Total)> ListAsync(SampleTimelineSearch search, List<string>? patientIds, CancellationToken ct = default);
    Task<SampleTimelineRecord?> GetAsync(string code, List<string>? patientIds, CancellationToken ct = default);
    Task<(List<SampleTimelineRfid> Items, long Total)> RfidAsync(SampleTimelineSummary sample, int page, int pageSize, CancellationToken ct = default);
}
