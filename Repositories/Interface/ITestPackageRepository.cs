
using Testorder_service.Models.Entities;

namespace TestOrder.Repositories.Interface
{
    public interface ITestPackageRepository
    {
        Task<IEnumerable<TestPackage>> GetAllAsync();
        Task<TestPackage?> GetByIdAsync(string id);
        Task AddAsync(TestPackage entity);
        Task UpdateAsync(TestPackage entity);
        Task DeleteAsync(string id);
        Task SaveChangesAsync();
        Task<List<TestCriterion>> GetPakageCriterion(string packageId);
    }
}
