using Testorder_service.Models.Entities;

namespace TestOrder.Repositories.Interface
{
    public interface ITestResultRepository
    {
        Task<IEnumerable<TestResult>> GetAllAsync();
        Task<TestResult?> GetByIdAsync(string id);
        Task AddAsync(TestResult entity);
        Task UpdateAsync(TestResult entity);
        Task DeleteAsync(string id);
        Task SaveChangesAsync();
        Task<IEnumerable<TestResult>> GetByAppointmentIdAsync(string appointmentId);

    }
}
