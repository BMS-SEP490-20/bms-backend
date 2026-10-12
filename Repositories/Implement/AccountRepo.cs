using iam_service.Modals;
using iam_service.Modals.Dto;
using iam_service.Repositories.Interface;
using iam_service.Data;
using Microsoft.EntityFrameworkCore;
using IamService.Services.Interface;

// Để dùng IPasswordHasherService

namespace iam_service.Repositories.Implement
{
    public class AccountRepo : IAccountRepo
    {
        private readonly UserManagementDBContext _context;
        private readonly IPasswordHasherService _hasherService;

        public AccountRepo(UserManagementDBContext context, IPasswordHasherService hasherService)
        {
            _context = context;
            _hasherService = hasherService;
        }

        public async Task<IEnumerable<Account>> GetAllAsync()
        {
            // AsNoTracking giúp tăng hiệu năng khi chỉ đọc dữ liệu
            return await _context.Account.AsNoTracking().ToListAsync();
        }

        public async Task<Account?> GetByIdAsync(string id)
        {
            return await _context.Account.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        }
        public async Task<Account?> GetByEmailAsync(string email)
        {
            return await _context.Account.AsNoTracking().FirstOrDefaultAsync(a => a.Email == email);
        }

        public async Task<Account> CreateAsync(Account newAccount)
        {
            // Đảm bảo mật khẩu đã được hash trước khi lưu
            if (!string.IsNullOrEmpty(newAccount.Password))
            {
                newAccount.Password = _hasherService.HashPassword(newAccount.Password);
            }

            await _context.Account.AddAsync(newAccount);
            await _context.SaveChangesAsync();
            return newAccount;
        }

        public async Task<Account?> UpdateAsync(string id, AccountDto accountDto)
        {
            var account = await _context.Account.FirstOrDefaultAsync(a => a.Id == id);
            if (account == null) return null;

            account.Username = accountDto.Username;
            account.Email = accountDto.Email;
            account.RoleId = accountDto.RoleId;
            account.Status = accountDto.Status;

            if (!string.IsNullOrEmpty(accountDto.Password))
            {
                account.Password = _hasherService.HashPassword(accountDto.Password);
            }

            await _context.SaveChangesAsync();
            return account;
        }

        public async Task<bool> DeleteAsync(string id)
        {
            var account = await _context.Account.FirstOrDefaultAsync(a => a.Id == id);
            if (account == null) return false;

            _context.Account.Remove(account);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
