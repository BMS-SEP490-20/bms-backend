namespace BMS.Core.Exceptions;

/// <summary>Lỗi nghiệp vụ xác thực. GlobalExceptionHandler map sang ProblemDetails.</summary>
public abstract class AuthException(int statusCode, string code, string message)
    : AppException(statusCode, code, message);

/// <summary>Sai tài khoản HOẶC sai mật khẩu – dùng chung 1 thông báo để chống dò tài khoản.</summary>
public sealed class InvalidCredentialsException()
    : AuthException(401, "INVALID_CREDENTIALS", "Tên đăng nhập hoặc mật khẩu không đúng.");

public sealed class AccountLockedException(TimeSpan retryAfter)
    : AuthException(423, "ACCOUNT_LOCKED",
        "Tài khoản tạm thời bị khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

public sealed class AccountDisabledException()
    : AuthException(403, "ACCOUNT_DISABLED", "Tài khoản đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.");

/// <summary>Tài khoản không có role hợp lệ (1–6) nên không được cấp token (TIEU-CHI R8).</summary>
public sealed class RoleNotAssignedException()
    : AuthException(403, "ROLE_NOT_ASSIGNED", "Tài khoản chưa được gán vai trò hợp lệ. Vui lòng liên hệ quản trị viên.");

public sealed class InvalidRefreshTokenException()
    : AuthException(401, "INVALID_REFRESH_TOKEN", "Phiên đăng nhập không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.");
