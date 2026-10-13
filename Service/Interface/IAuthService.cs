// Services/Interface/IAuthService.cs
using iam_service.Modals.Dto;

namespace IamService.Services.Interface
{
    public interface IAuthService
    {
        Task<LoginResponseDto?> LoginAsync(string username, string password);
        Task<string?> RegisterAsync(string email);
        Task<bool> SendResetPasswordOtpAsync(string email);
        Task<bool> ResetPasswordAsync(ResetPasswordSimple reset);
        Task<bool> Verifyotp(VerifyOtpDto verifyOtp);
        Task<bool> SendOtpAsync(string email);
        //Task<QuickRegisterResponse> QuickRegister(
        //   QuickRegisterRequest request,
        //   ServerCallContext context);
        //Task<QuickRegisterResponse> QuickRegister(QuickRegisterRequest quickRegisterRequest);
    }
}
