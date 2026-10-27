using TestOrder.Models.DTO;
using Testorder_service.Models.DTO;

namespace TestOrder.Services.Interface
{
    public interface ITestResultService
    {
        Task<IEnumerable<TestResultDto>> GetAllAsync();
        Task<TestResultDto?> GetByIdAsync(string id);
        Task CreateAsync(CreateFullTestResultDto dto);
        Task UpdateAsync(string id, UpdateTestResultDto dto);
        Task DeleteAsync(string id);
        Task<IEnumerable<TestResultDto>> GetByAppointmentIdAsync(string appointmentId);

        /// <summary>
        /// Reviews, verifies, and digitally approves a blood test result.
        /// </summary>
        /// <param name="id">Test result identifier.</param>
        /// <param name="dto">Approval metadata including approver identity, role, and digital signature.</param>
        /// <returns>The approved test result data transfer object.</returns>
        Task<TestResultDto> ApproveAsync(string id, ApproveTestResultDto dto);
    }
}
