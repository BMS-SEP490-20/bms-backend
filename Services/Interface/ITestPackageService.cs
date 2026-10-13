using TestOrder.Models.DTO;
using Testorder_service.Models.Entities;

namespace TestOrder.Services.Interface
{
    public interface ITestPackageService
    {
        Task<IEnumerable<TestPackageDto>> GetAllAsync();
        Task<TestPackageDto?> GetByIdAsync(string id);
        Task CreateAsync(CreateTestPackageDto dto);
        Task UpdateAsync(string id, UpdateTestPackageDto dto);
        Task DeleteAsync(string id);
        Task<List<TestCriterion>> GetPakageCriterion(string packageId);
    }
}
