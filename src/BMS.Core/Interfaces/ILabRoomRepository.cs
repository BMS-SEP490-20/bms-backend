using Testorder_service.Models.Entities;

namespace Testorder_service.Repositories.Interface
{
    public interface ILabRoomRepository
    {
        Task<IEnumerable<LabRoom>> GetAllAsync();
        Task<LabRoom?> GetByIdAsync(string id);
        Task AddAsync(LabRoom room);
        Task UpdateAsync(LabRoom room);
        Task DeleteAsync(LabRoom room);
        Task SaveChangesAsync();
    }
}
