using iam_service.Modals;
using iam_service.Modals.Dto;


namespace iam_service.Repositories.Interface
{
    public interface IAuthRepo
    {
        // Xác minh thông tin đăng nhập và trả về User nếu thành công
        Task<Account?> VerifyLoginAsync(string username, string password);

        // Đăng ký user mới và trả về Account đã tạo
        Task<Account> RegisterAccountAsync(Account newAccount);

        // Kiểm tra email đã tồn tại
        Task<bool> IsEmailExistAsync(string email);

        // Cập nhật thời gian đăng nhập cuối
        Task UpdateLastLoginAsync(Account user);

        // Lấy tài khoản theo email
        Task<Account?> GetByEmailAsync(string email);

        // Cập nhật mật khẩu tài khoản
        Task UpdatePasswordAsync(Account user);

        // Đăng nhập, trả về token và thông báo
        Task<LoginResponseDto?> LoginAsync(string username, string password);

        // Đăng ký nhanh bằng email, trả về thông báo hoặc mã tài khoản
        Task<string?> QuickRegisterAsync(string email);

        // Gửi OTP reset password
        Task<bool> SendResetPasswordOtpAsync(string email);

        // Xác thực OTP và đổi mật khẩu mới
        Task<bool> ResetPasswordAsync(ResetPasswordSimple reset);
        Task<bool> Verifyotp(VerifyOtpDto verifyOtp);
        Task<bool> SendOtpAsync(string email);
    }
}
