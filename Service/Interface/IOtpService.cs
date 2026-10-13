namespace iam_service.Service.Interface
{
    public interface IOtpService
    {
        Task<bool> GenerateAndSendOtpAsync(string email);
        Task<bool> VerifyOtpAsync(string email, string otpCode);
    }

}
