using TestOrder.Models.DTO;
using Testorder_service.Models.Entities;

namespace TestOrder.Services.Interface
{
    public interface IAppointmentService
    {
        Task<IEnumerable<AppointmentReadDto>> GetAllAsync();
        Task<AppointmentReadDto?> GetByIdAsync(string id);
        Task<AppointmentReadDto> CreateAsync(AppointmentCreateDto dto);
        Task<bool> UpdateAsync(string id, AppointmentUpdateDto dto);
        Task<bool> DeleteAsync(string id);
        Task<IEnumerable<AppointmentReadDto>> GetByPatientIdAsync(string patientId);
    }
}
