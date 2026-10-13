namespace BMS.Core.Exceptions;

/// <summary>Token đặt lại mật khẩu sai / đã dùng / hết hạn – dùng chung 1 thông báo.</summary>
public sealed class InvalidResetTokenException()
    : AppException(400, "INVALID_RESET_TOKEN", "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
