// Services/Implement/AuthService.cs
using iam_service.Modals.Dto;
using iam_service.Repositories.Interface;
using IamService.Services.Interface;

namespace IamService.Services.Implement
{
    public class AuthService : IAuthService
    {
        private readonly IAuthRepo _authRepo;

        public AuthService(IAuthRepo authRepo)
        {
            _authRepo = authRepo;
        }

        public Task<LoginResponseDto?> LoginAsync(string username, string password)
        {
            return _authRepo.LoginAsync(username, password);
        }

        public Task<string?> RegisterAsync(string email)
        {
            return _authRepo.QuickRegisterAsync(email);
        }

        public Task<bool> SendResetPasswordOtpAsync(string email)
        {
            return _authRepo.SendResetPasswordOtpAsync(email);
        }

        public Task<bool> ResetPasswordAsync(ResetPasswordSimple reset)
        {
            return _authRepo.ResetPasswordAsync(reset);
        }
        public Task<bool> Verifyotp(VerifyOtpDto verifyOtp)
        {
            return _authRepo.Verifyotp(verifyOtp);
        }

        //public async Task<QuickRegisterResponse> QuickRegister(QuickRegisterRequest quickRegisterRequest)
        //{
        //    var message = await _authRepo.QuickRegisterAsync(quickRegisterRequest.Email);
        //    return new QuickRegisterResponse { Message = message };
        //}
        public Task<bool> SendOtpAsync(string email)
        {
            return _authRepo.SendOtpAsync(email);
        }

    }
}
