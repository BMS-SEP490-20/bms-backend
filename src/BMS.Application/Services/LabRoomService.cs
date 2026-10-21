using TestOrder.Models.DTO;
using TestOrder.Repositories.Interface;
using TestOrder.Services.Interface;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;
using Testorder_service.Repositories.Interface;
using Testorder_service.Services.Interface;

namespace TestOrder.Services.Implement
{
    public class LabRoomService : ILabRoomService
    {
        private readonly ILabRoomRepository _repo;

        public LabRoomService(ILabRoomRepository repo)
        {
            _repo = repo;
        }

        public async Task<IEnumerable<LabRoomDto>> GetAllAsync()
        {
            var rooms = await _repo.GetAllAsync();
            return rooms.Select(r => new LabRoomDto
            {
                RoomId = r.RoomId,
                RoomName = r.RoomName,
                RoomLocation = r.RoomLocation,
                RoomType = r.RoomType,
                IsAvailable = r.IsAvailable,
                CreatedAt = r.CreatedAt
            });
        }

        public async Task<LabRoomDto?> GetByIdAsync(string id)
        {
            var room = await _repo.GetByIdAsync(id);
            if (room == null) return null;

            return new LabRoomDto
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                RoomLocation = room.RoomLocation,
                RoomType = room.RoomType,
                IsAvailable = room.IsAvailable,
                CreatedAt = room.CreatedAt
            };
        }

        public async Task CreateAsync(CreateLabRoomDto dto)
        {
            var room = new LabRoom
            {
                RoomId = await GenerateRoomId(),
                RoomName = dto.RoomName,
                RoomLocation = dto.RoomLocation,
                RoomType = dto.RoomType,
                IsAvailable = dto.IsAvailable ?? true,
                AssignedEquipment = dto.AssignedEquipment,
                TechnicianInCharge = dto.TechnicianInCharge,
                CreatedAt = DateTime.UtcNow
            };

            await _repo.AddAsync(room);
            await _repo.SaveChangesAsync();
        }

        public async Task UpdateAsync(string id, UpdateLabRoomDto dto)
        {
            var room = await _repo.GetByIdAsync(id);
            if (room == null) throw new KeyNotFoundException("Không tìm thấy phòng.");

            room.RoomName = dto.RoomName ?? room.RoomName;
            room.RoomLocation = dto.RoomLocation ?? room.RoomLocation;
            room.RoomType = dto.RoomType ?? room.RoomType;
            room.IsAvailable = dto.IsAvailable ?? room.IsAvailable;
            room.AssignedEquipment = dto.AssignedEquipment ?? room.AssignedEquipment;
            room.TechnicianInCharge = dto.TechnicianInCharge ?? room.TechnicianInCharge;

            await _repo.UpdateAsync(room);
            await _repo.SaveChangesAsync();
        }

        public async Task DeleteAsync(string id)
        {
            var room = await _repo.GetByIdAsync(id);
            if (room == null) throw new KeyNotFoundException("Không tìm thấy phòng.");

            await _repo.DeleteAsync(room);
            await _repo.SaveChangesAsync();
        }
        private async Task<string> GenerateRoomId()
        {
            var rooms = await _repo.GetAllAsync();
            var existingIds = rooms.Select(r => r.RoomId)
                                 .Where(id => id.StartsWith("R"))
                                 .Select(id => int.TryParse(id[1..], out int num) ? num : 0)
                                 .DefaultIfEmpty(0)
                                 .Max();
            return $"R{(existingIds + 1):D3}";
        }
    }
}
