
using Testorder_service.Models.Entities;

namespace TestOrder.Repositories.Interface
{
    public interface IBloodSampleRepository
    {
        Task<IEnumerable<BloodSample>> GetAllAsync();
        Task<BloodSample?> GetByIdAsync(string id);
        Task<BloodSample> CreateAsync(BloodSample entity);
        Task UpdateAsync(BloodSample entity);
        Task DeleteAsync(BloodSample entity);
        Task<IEnumerable<BloodSample>> GetByAppointmentIdAsync(string appointmentId);

    }
}
