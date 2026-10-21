using BMS.Application.DTOs.LabProfiles;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;

namespace BMS.Application.Services;

/// <summary>UC61 profile use cases, scoped to professional details rather than login credentials.</summary>
public sealed class LabProfileService(ILabProfileRepository repository, TimeProvider clock) : ILabProfileService
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
    public async Task<LabProfileAccountsDto> AccountsAsync(string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (search?.Length > 100 || page < 1 || page > 10000 || pageSize < 1 || pageSize > 100)
            throw new DomainValidationException("INVALID_LAB_ACCOUNT_FILTER", "Bộ lọc tài khoản không hợp lệ.");
        var (items, total) = await repository.AccountsAsync(search?.Trim(), page, pageSize, ct);
        return new(items, total, page, pageSize, (int)Math.Ceiling((double)total / pageSize));
    }
    public async Task<LabProfileDto> CreateAsync(CreateLabProfileDto input, LabProfileActor actor, CancellationToken ct = default)
    {
        ValidateId(input.AccountId); ValidateActor(actor);
        var details = ValidateDetails(input.FullName, input.Qualification, input.AssignedRoom, input.Reason);
        var account = await repository.EligibleAccountAsync(input.AccountId.Trim(), ct)
            ?? throw new DomainValidationException("INVALID_LAB_ACCOUNT", "Chọn tài khoản Lab đang hoạt động (kỹ thuật viên hoặc trưởng Lab).");
        if (account.HasProfile) throw new LabProfileConflictException("Tài khoản đã có hồ sơ Lab, kể cả hồ sơ đã lưu trữ.");
        var rooms = await ValidateRoom(details.AssignedRoom, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var profile = new LabUserProfile { Id = account.Id, FullName = details.FullName, Qualification = details.Qualification,
            AssignedRoom = details.AssignedRoom, CreatedAt = now, UpdatedAt = now,
            Audit = [new("Create", actor.Id, actor.Name, now, input.Reason.Trim(), null, details)] };
        if (!await repository.InsertAsync(profile, ct)) throw new LabProfileConflictException("Tài khoản vừa được tạo hồ sơ. Vui lòng tải lại.");
        return Map(profile, rooms);
    }
    public async Task<LabProfileDto> UpdateAsync(string id, UpdateLabProfileDto input, LabProfileActor actor, CancellationToken ct = default)
    {
        ValidateId(id); ValidateActor(actor); ValidateRevision(input.ExpectedRevision);
        var details = ValidateDetails(input.FullName, input.Qualification, input.AssignedRoom, input.Reason);
        var current = await repository.GetAsync(id.Trim(), ct) ?? throw new LabProfileNotFoundException();
        if (current.Revision != input.ExpectedRevision) throw Stale();
        if (await repository.EligibleAccountAsync(current.Id, ct) is null)
            throw new DomainValidationException("INVALID_LAB_ACCOUNT", "Tài khoản liên kết không còn là nhân viên Lab đang hoạt động.");
        var rooms = await ValidateRoom(details.AssignedRoom, ct);
        var audit = new LabProfileAudit("Update", actor.Id, actor.Name, clock.GetUtcNow().UtcDateTime, input.Reason.Trim(),
            new(current.FullName, current.Qualification, current.AssignedRoom), details);
        var updated = await repository.UpdateAsync(current.Id, input.ExpectedRevision!.Value, details, audit, ct) ?? throw Stale();
        return Map(updated, rooms);
    }
    private static LabProfileConflictException Stale() => new("Hồ sơ đã được thay đổi. Hãy tải lại dữ liệu trước khi thử lại.");
    private static void ValidateRevision(long? revision)
    {
        if (!revision.HasValue || revision < 0 || revision == long.MaxValue)
            throw new DomainValidationException("INVALID_LAB_PROFILE_REVISION", "Cần phiên bản hồ sơ hợp lệ. Hãy tải lại dữ liệu.");
    }
    private static void ValidateActor(LabProfileActor actor)
    {
        if (string.IsNullOrWhiteSpace(actor.Id)) throw new ForbiddenAccessException("Không xác định được người thực hiện.");
    }
    private static LabProfileSnapshot ValidateDetails(string fullName, string qualification, string room, string reason)
    {
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 200 || string.IsNullOrWhiteSpace(qualification) || qualification.Length > 500 ||
            string.IsNullOrWhiteSpace(room) || room.Length > 100 || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5 || reason.Length > 500)
            throw new DomainValidationException("INVALID_LAB_PROFILE", "Họ tên (tối đa 200), chuyên môn (tối đa 500), phòng và lý do (5–500 ký tự) là bắt buộc.");
        return new(fullName.Trim(), qualification.Trim(), room.Trim());
    }
    private async Task<List<LabProfileRoom>> ValidateRoom(string room, CancellationToken ct)
    {
        var rooms = await repository.RoomsAsync(ct);
        if (!rooms.Any(r => r.Id == room)) throw new DomainValidationException("INVALID_LAB_ROOM", "Phòng Lab không tồn tại hoặc không còn hoạt động.");
        return rooms;
    }
    private static void ValidateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) throw new DomainValidationException("INVALID_LAB_PROFILE_ID", "Mã hồ sơ không hợp lệ.");
    }
    private static LabProfileDto Map(LabUserProfile p, List<LabProfileRoom> rooms) => new(p.Id, p.FullName, p.Qualification,
        p.AssignedRoom, rooms.FirstOrDefault(r => r.Id == p.AssignedRoom)?.Name ?? p.AssignedRoom, p.Revision, p.CreatedAt, p.UpdatedAt);
}
