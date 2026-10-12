using iam_service.Modals;
using iam_service.Modals.Dto;
using iam_service.Repositories.Interface;

using IamService.Services.Interface;

namespace IamService.Services.Implement
{
    public class AccountService : IAccountService
    {
        private readonly IAccountRepo _accountRepo;
        private readonly IPasswordHasherService _hasherService; // Có thể cần khi xử lý đổi mật khẩu

        public AccountService(
            IAccountRepo accountRepo, 
            IPasswordHasherService hasherService)
        {
            _accountRepo = accountRepo;
            _hasherService = hasherService;
        }

        public Task<IEnumerable<Account>> GetAllAsync()
        {
            return _accountRepo.GetAllAsync();
        }

        public Task<Account?> GetByIdAsync(string id)
        {
            return _accountRepo.GetByIdAsync(id);
        }

        public async Task<Account> CreateAsync(AccountDto accountDto)
        {
            // Logic nghiệp vụ trước khi tạo: kiểm tra tính hợp lệ, tạo password hash
            var newAccount = new Account
            {
                Id = Guid.NewGuid().ToString(),
                Username = accountDto.Username,
                Email = accountDto.Email,
                RoleId = accountDto.RoleId,
                Status = accountDto.Status,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                Password = accountDto.Password
            };
            // 2️⃣ Lưu vào DB
            var created = await _accountRepo.CreateAsync(newAccount);

            return created;
        }


        public Task<Account?> UpdateAsync(string id, AccountDto accountDto)
        {
            return _accountRepo.UpdateAsync(id, accountDto);
        }

        public Task<bool> DeleteAsync(string id)
        {
            return _accountRepo.DeleteAsync(id);
        }
    }
}