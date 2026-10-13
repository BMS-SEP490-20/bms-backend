using Microsoft.EntityFrameworkCore;
using TestOrder.Repositories.Interface;
using Testorder_service.Data;
using Testorder_service.Models.Entities;

namespace TestOrder.Repositories.Implement
{
    public class TestPackageRepository : ITestPackageRepository
    {
        private readonly TestOrderManagementDbContext _context;

        public TestPackageRepository(TestOrderManagementDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<TestPackage>> GetAllAsync()
            => await _context.TestPackages.ToListAsync();

        public async Task<TestPackage?> GetByIdAsync(string id)
            => await _context.TestPackages.FindAsync(id);

        public async Task AddAsync(TestPackage entity)
        {
            await _context.TestPackages.AddAsync(entity);
        }

        public async Task UpdateAsync(TestPackage entity)
        {
            _context.TestPackages.Update(entity);
            await Task.CompletedTask;
        }

        public async Task DeleteAsync(string id)
        {
            var item = await GetByIdAsync(id);
            if (item != null)
                _context.TestPackages.Remove(item);
        }
        public async Task<List<TestCriterion>> GetPakageCriterion(string packageId)
        {
            return await _context.TestPackageCriteria
                .Where(pc => pc.TestPackageId == packageId)
                
                .Select(pc => pc.TestCriteria)
                .ToListAsync();
        }

        public async Task SaveChangesAsync()
            => await _context.SaveChangesAsync();
    }
}
