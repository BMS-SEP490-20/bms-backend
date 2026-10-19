
using Testorder_service.Models.Entities;

namespace TestOrder.Repositories.Interface
{
    public interface IAppointmentRepository
    {
        Task<IEnumerable<Appointment>> GetAllAsync();
        Task<Appointment?> GetByIdAsync(string id);
        Task<Appointment> CreateAsync(Appointment entity);
        Task UpdateAsync(Appointment entity);
        Task DeleteAsync(Appointment entity);
        Task<IEnumerable<Appointment>> GetByPatientIdAsync(string patientId);
    }
}
