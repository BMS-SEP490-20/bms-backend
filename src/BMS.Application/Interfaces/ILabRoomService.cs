using BMS.Application.DTOs.Laboratory;

namespace BMS.Application.Interfaces
{
    /// <summary>Business operations for laboratory rooms (#43).</summary>
    public interface ILabRoomService
    {
        /// <summary>Returns one page of active rooms and the total number of active rooms.</summary>
        Task<(IReadOnlyList<LabRoomDto> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);

        /// <summary>Returns a room by id, or null when it does not exist.</summary>
        Task<LabRoomDto?> GetByIdAsync(string id, CancellationToken ct = default);

        /// <summary>Creates a room with a server-generated <c>R###</c> id.</summary>
        Task<LabRoomDto> CreateAsync(CreateLabRoomDto dto, CancellationToken ct = default);

        /// <summary>Updates a room; throws <see cref="BMS.Core.Exceptions.ResourceNotFoundException"/> when missing.</summary>
        Task UpdateAsync(string id, UpdateLabRoomDto dto, CancellationToken ct = default);

        /// <summary>Soft-deletes a room; throws <see cref="BMS.Core.Exceptions.ResourceNotFoundException"/> when missing.</summary>
        Task DeleteAsync(string id, CancellationToken ct = default);
    }
}
