namespace BMS.Core.Exceptions;

/// <summary>
/// Lỗi nghiệp vụ có mã HTTP + mã lỗi máy đọc được (code).
/// GlobalExceptionHandler map sang ProblemDetails.
/// </summary>
public abstract class AppException(int statusCode, string code, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}
