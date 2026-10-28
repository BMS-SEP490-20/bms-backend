using TestOrder.Models.DTO;

namespace TestOrder.Services.Interface
{
    public interface IBloodSampleService
    {
        Task<IEnumerable<BloodSampleReadDto>> GetAllAsync();
        Task<BloodSampleReadDto?> GetByIdAsync(string id);
        Task<BloodSampleReadDto> CreateAsync(BloodSampleCreateDto dto);
        Task<bool> UpdateAsync(string id, BloodSampleUpdateDto dto);
        Task<bool> DeleteAsync(string id);
        Task<bool> ValidateSampleAsync(string id, string status, string notes, string labUserId);
        Task<IEnumerable<BloodSampleReadDto>> GetBloodSampleByAppointmentIdAsync(string appointmentId);
    }
}
