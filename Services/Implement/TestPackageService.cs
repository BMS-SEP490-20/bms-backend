using TestOrder.Models.DTO;
using TestOrder.Repositories.Interface;
using TestOrder.Services.Interface;
using Testorder_service.Models.Entities;

namespace TestOrder.Services.Implement
{
    public class TestPackageService : ITestPackageService
    {
        private readonly ITestPackageRepository _repo;
        private readonly ITestPackageCriterionRepository _criterionRepo;
        public TestPackageService(ITestPackageRepository repo, ITestPackageCriterionRepository testPackageCriterion)
        {
            _repo = repo;
            _criterionRepo = testPackageCriterion;
        }

        public async Task<IEnumerable<TestPackageDto>> GetAllAsync()
        {
            var packages = await _repo.GetAllAsync();
            return packages.Select(p => new TestPackageDto
            {
                PackageId = p.PackageId,
                PackageName = p.PackageName,
                Description = p.Description,
                Price = p.Price,
                Status = p.Status,
                CreatedAt = p.CreatedAt
            });
        }

        public async Task<TestPackageDto?> GetByIdAsync(string id)
        {
            var p = await _repo.GetByIdAsync(id);
            if (p == null) return null;

            return new TestPackageDto
            {
                PackageId = p.PackageId,
                PackageName = p.PackageName,
                Description = p.Description,
                Price = p.Price,
                Status = p.Status,
                CreatedAt = p.CreatedAt
            };
        }

        public async Task CreateAsync(CreateTestPackageDto dto)
        {
            var random = new Random();

            // 1) Tạo ID gói xét nghiệm TPxxxxx
            string packageId = "TP" + random.Next(10000, 99999);

            // 2) Tạo TestPackage
            var package = new TestPackage
            {
                PackageId = packageId,
                PackageName = dto.PackageName,
                Description = dto.Description,
                Price = dto.Price,
                Status = dto.Status,
                CreatedAt = DateTime.Now
            };

            await _repo.AddAsync(package);

            // Removed criterion logic as per user request (basic CRUD only)
            
            // 4) Save 1 lần
            await _repo.SaveChangesAsync();
        }


        public async Task UpdateAsync(string id, UpdateTestPackageDto dto)
        {
            // 1) Lấy package từ DB
            var package = await _repo.GetByIdAsync(id);
            if (package == null)
                throw new Exception("TestPackage not found.");

            // 2) Update thông tin gói xét nghiệm
            package.PackageName = dto.PackageName ?? package.PackageName;
            package.Description = dto.Description ?? package.Description;
            package.Price = dto.Price ?? package.Price;
            package.Status = dto.Status ?? package.Status;

            await _repo.UpdateAsync(package);

            // Removed criterion logic as per user request (basic CRUD only)
            
            // 7) Save
            await _repo.SaveChangesAsync();
        }


        public async Task DeleteAsync(string id)
        {
            await _repo.DeleteAsync(id);
            await _repo.SaveChangesAsync();
        }

        public Task<List<TestCriterion>> GetPakageCriterion(string packageId)
        {
            return _repo.GetPakageCriterion(packageId);
        }
    }
}
