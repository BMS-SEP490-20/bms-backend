using BMS.Application.DTOs.LabProfiles;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>UC61 profile use cases, scoped to professional details rather than login credentials.</summary>
public sealed class LabProfileService(ILabProfileRepository repository) : ILabProfileService
{
    public async Task<LabProfileListDto> ListAsync(LabProfileFilterDto filter, CancellationToken ct = default)
    {
        if (filter.Page < 1 || filter.Page > 10000 || filter.PageSize < 1 || filter.PageSize > 100 || filter.Search?.Length > 100 || filter.RoomId?.Length > 100 ||
            (filter.CreatedFrom.HasValue && filter.CreatedTo.HasValue && filter.CreatedFrom.Value.Date > filter.CreatedTo.Value.Date) ||
            filter.CreatedTo?.Date == DateTime.MaxValue.Date || filter.CreatedFrom?.Date == DateTime.MinValue.Date)
            throw new DomainValidationException("INVALID_LAB_PROFILE_FILTER", "Bộ lọc hoặc khoảng ngày không hợp lệ.");
        var (items, total) = await repository.ListAsync(new(filter.Search?.Trim(), filter.RoomId?.Trim(), filter.CreatedFrom, filter.CreatedTo, filter.Page, filter.PageSize), ct);
        var rooms = await repository.RoomsAsync(ct);
        return new(items.Select(p => Map(p, rooms)).ToList(), total, filter.Page, filter.PageSize, (int)Math.Ceiling((double)total / filter.PageSize));
    }

    public async Task<LabProfileDto?> GetAsync(string id, CancellationToken ct = default)
    {
        ValidateId(id);
        var profile = await repository.GetAsync(id.Trim(), ct);
        return profile is null ? null : Map(profile, await repository.RoomsAsync(ct));
    }

    public Task<List<LabProfileRoom>> RoomsAsync(CancellationToken ct = default) => repository.RoomsAsync(ct);
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) throw new DomainValidationException("INVALID_LAB_PROFILE_ID", "Mã hồ sơ không hợp lệ.");
    }
    private static LabProfileDto Map(LabUserProfile p, List<LabProfileRoom> rooms) => new(p.Id, p.FullName, p.Qualification,
        p.AssignedRoom, rooms.FirstOrDefault(r => r.Id == p.AssignedRoom)?.Name ?? p.AssignedRoom, p.Revision, p.CreatedAt, p.UpdatedAt);
}
