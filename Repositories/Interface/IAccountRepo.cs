using iam_service.Modals;
using iam_service.Modals.Dto;


namespace iam_service.Repositories.Interface
{
    public interface IAccountRepo
    {
        Task<IEnumerable<Account>> GetAllAsync();
        Task<Account?> GetByIdAsync(string id);

        Task<Account?> GetByEmailAsync(string email);
        Task<Account> CreateAsync(Account newAccount);
        Task<Account?> UpdateAsync(string id, AccountDto accountDto);
        Task<bool> DeleteAsync(string id);
        
    }
}