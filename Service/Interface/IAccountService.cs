using iam_service.Modals.Dto;
using iam_service.Modals;


namespace IamService.Services.Interface
{
    public interface IAccountService
    {
        Task<IEnumerable<Account>> GetAllAsync();
        Task<Account?> GetByIdAsync(string id);
        Task<Account> CreateAsync(AccountDto accountDto);
        Task<Account?> UpdateAsync(string id, AccountDto accountDto);
        Task<bool> DeleteAsync(string id);

    }
}