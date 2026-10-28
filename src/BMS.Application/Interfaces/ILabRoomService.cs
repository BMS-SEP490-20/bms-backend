using Testorder_service.Models.DTO;

namespace Testorder_service.Services.Interface
{
    public interface ILabRoomService
    {
        Task<IEnumerable<LabRoomDto>> GetAllAsync();
        Task<LabRoomDto?> GetByIdAsync(string id);
        Task CreateAsync(CreateLabRoomDto dto);
        Task UpdateAsync(string id, UpdateLabRoomDto dto);
        Task DeleteAsync(string id);
    }
}
