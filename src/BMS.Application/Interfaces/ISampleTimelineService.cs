using BMS.Application.DTOs.Laboratory;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

/// <summary>UC71 evidence-based timeline with server-enforced patient scope and staff-only RFID metadata.</summary>
public interface ISampleTimelineService
{
    Task<SampleTimelinePage<SampleTimelineSummary>> ListAsync(SampleTimelineFilterDto filter, SampleTimelineViewer viewer, CancellationToken ct = default);
    Task<SampleTimelineDto?> GetAsync(string code, SampleTimelinePageQuery page, SampleTimelineViewer viewer, CancellationToken ct = default);
    Task<SampleTimelinePage<SampleTimelineRfid>?> RfidAsync(string code, SampleTimelinePageQuery page, SampleTimelineViewer viewer, CancellationToken ct = default);
}
