using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>Data access contract for laboratory rooms (#43). All reads exclude soft-deleted rooms.</summary>
public interface ILabRoomRepository
{
    /// <summary>Returns one page of active rooms ordered by id, plus the total count.</summary>
    Task<(IReadOnlyList<LabRoom> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>Returns an active room by id, or null.</summary>
    Task<LabRoom?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Returns the highest sequence number used by <c>R###</c> room ids (0 when none).</summary>
    Task<int> GetMaxRoomSequenceAsync(CancellationToken ct = default);

    /// <summary>Inserts a room; returns false when the id already exists (duplicate key).</summary>
    Task<bool> TryAddAsync(LabRoom room, CancellationToken ct = default);

    /// <summary>Atomically updates editable room fields; returns false when the room does not exist.</summary>
    Task<bool> UpdateAsync(LabRoom room, CancellationToken ct = default);

    /// <summary>Soft-deletes a room with an atomic <c>$set</c>; returns false when the room does not exist.</summary>
    Task<bool> SoftDeleteAsync(string id, CancellationToken ct = default);
}
