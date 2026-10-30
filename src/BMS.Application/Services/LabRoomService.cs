using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;

namespace BMS.Application.Services
{
    /// <summary>Lab room management (#43): listing, creation, update and soft delete.</summary>
    public class LabRoomService : ILabRoomService
    {
        private const string NotFoundCode = "LAB_ROOM_NOT_FOUND";
        private const string NotFoundMessage = "Không tìm thấy phòng.";
        private const int MaxIdAttempts = 5;
        private readonly ILabRoomRepository _repo;

        /// <summary>Creates the service over the lab room repository.</summary>
        public LabRoomService(ILabRoomRepository repo)
        {
            _repo = repo;
        }

        /// <inheritdoc />
        public async Task<(IReadOnlyList<LabRoomDto> Items, long Total)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
        {
            var (rooms, total) = await _repo.GetPagedAsync(page, pageSize, ct);
            return (rooms.Select(ToDto).ToList(), total);
        }

        /// <inheritdoc />
        public async Task<LabRoomDto?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var room = await _repo.GetByIdAsync(id, ct);
            return room is null ? null : ToDto(room);
        }

        /// <inheritdoc />
        public async Task<LabRoomDto> CreateAsync(CreateLabRoomDto dto, CancellationToken ct = default)
        {
            // Ids are generated from the current max sequence; a concurrent insert of the same id hits the
            // unique _id index, so we retry with a fresh sequence instead of loading every room into memory.
            for (var attempt = 0; attempt < MaxIdAttempts; attempt++)
            {
                var room = new LabRoom
                {
                    RoomId = $"R{await _repo.GetMaxRoomSequenceAsync(ct) + 1:D3}",
                    RoomName = dto.RoomName.Trim(),
                    RoomLocation = dto.RoomLocation,
                    RoomType = dto.RoomType,
                    IsAvailable = dto.IsAvailable ?? true,
                    AssignedEquipment = dto.AssignedEquipment,
                    TechnicianInCharge = dto.TechnicianInCharge,
                    CreatedAt = DateTime.UtcNow
                };
                if (await _repo.TryAddAsync(room, ct)) return ToDto(room);
            }
            throw new DomainValidationException("LAB_ROOM_ID_CONFLICT", "Không thể cấp mã phòng, vui lòng thử lại.");
        }

        /// <inheritdoc />
        public async Task UpdateAsync(string id, UpdateLabRoomDto dto, CancellationToken ct = default)
        {
            var room = await _repo.GetByIdAsync(id, ct) ?? throw new ResourceNotFoundException(NotFoundCode, NotFoundMessage);

            room.RoomName = dto.RoomName ?? room.RoomName;
            room.RoomLocation = dto.RoomLocation ?? room.RoomLocation;
            room.RoomType = dto.RoomType ?? room.RoomType;
            room.IsAvailable = dto.IsAvailable ?? room.IsAvailable;
            room.AssignedEquipment = dto.AssignedEquipment ?? room.AssignedEquipment;
            room.TechnicianInCharge = dto.TechnicianInCharge ?? room.TechnicianInCharge;

            if (!await _repo.UpdateAsync(room, ct)) throw new ResourceNotFoundException(NotFoundCode, NotFoundMessage);
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken ct = default)
        {
            // Soft delete keeps historical shift / profile references to the room valid.
            if (!await _repo.SoftDeleteAsync(id, ct)) throw new ResourceNotFoundException(NotFoundCode, NotFoundMessage);
        }

        private static LabRoomDto ToDto(LabRoom r) => new()
        {
            RoomId = r.RoomId,
            RoomName = r.RoomName,
            RoomLocation = r.RoomLocation,
            RoomType = r.RoomType,
            IsAvailable = r.IsAvailable,
            CreatedAt = r.CreatedAt
        };
    }
}
