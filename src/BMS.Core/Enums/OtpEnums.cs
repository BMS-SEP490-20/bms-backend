namespace BMS.Core.Enums;

/// <summary>
/// Mục đích nghiệp vụ của mã xác thực OTP trong hệ thống BMS.
/// Tuân thủ: NIST SP 800-63B & OWASP Authentication Guidelines.
/// </summary>
public enum OtpPurpose
{
    /// <summary>
    /// Xác thực đăng ký tài khoản bệnh nhân hoặc nhân viên mới (UC02).
    /// </summary>
    Registration = 1,

    /// <summary>
    /// Xác thực yêu cầu cấp lại mật khẩu bị quên (UC10).
    /// </summary>
    PasswordReset = 2,

    /// <summary>
    /// Xác thực đăng nhập 2 yếu tố (2FA).
    /// </summary>
    Login = 3,

    /// <summary>
    /// Xác thực xác nhận giao dịch thanh toán viện phí hoặc đổi thông tin nhạy cảm.
    /// </summary>
    Transaction = 4
}

/// <summary>
/// Kênh truyền thông phát tán mã xác thực OTP tới người dùng.
/// </summary>
public enum OtpChannel
{
    /// <summary>
    /// Gửi mã qua dịch vụ thư điện tử (SMTP / SendGrid).
    /// </summary>
    Email = 1,

    /// <summary>
    /// Gửi mã qua tin nhắn SMS Brandname (Twilio / Viettel / VNPT).
    /// </summary>
    Sms = 2
}
