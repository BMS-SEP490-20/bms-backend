using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>Persists professional Lab profiles without changing the linked login account.</summary>
public interface ILabProfileRepository
{
    Task<(List<LabUserProfile> Items, long Total)> ListAsync(LabProfileSearch search, CancellationToken ct = default);
    Task<LabUserProfile?> GetAsync(string id, CancellationToken ct = default);
    Task<List<LabProfileRoom>> RoomsAsync(CancellationToken ct = default);
    Task<(List<LabProfileAccount> Items, long Total)> AccountsAsync(string? search, int page, int pageSize, CancellationToken ct = default);
    Task<LabProfileAccount?> EligibleAccountAsync(string id, CancellationToken ct = default);
    Task<bool> HasProfileAsync(string id, CancellationToken ct = default);
    Task<bool> InsertAsync(LabUserProfile profile, CancellationToken ct = default);
}

public sealed record LabProfileSearch(string? Search, string? RoomId, DateTime? CreatedFrom, DateTime? CreatedTo, int Page, int PageSize);
