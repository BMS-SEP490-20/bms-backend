using BMS.Application.DTOs.LabProfiles;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

/// <summary>UC61 professional Lab profile directory.</summary>
public interface ILabProfileService
{
    Task<LabProfileListDto> ListAsync(LabProfileFilterDto filter, CancellationToken ct = default);
    Task<LabProfileDto?> GetAsync(string id, CancellationToken ct = default);
    Task<List<LabProfileRoom>> RoomsAsync(CancellationToken ct = default);
    Task<LabProfileAccountsDto> AccountsAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<LabProfileDto> CreateAsync(CreateLabProfileDto input, LabProfileActor actor, CancellationToken ct = default);
    Task<LabProfileDto> UpdateAsync(string id, UpdateLabProfileDto input, LabProfileActor actor, CancellationToken ct = default);
}
