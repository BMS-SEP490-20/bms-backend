namespace BMS.Application.DTOs.Users;

/// <summary>
/// Data Transfer Object representing a sanitized clinical user profile (UC08).
/// Strictly omits cryptographic secrets (PasswordHash, PasswordSalt).
/// Conforms to: OWASP Top 10 (A01: IDOR Prevention) & GDPR PII Security.
/// </summary>
public class UserProfileResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PatientCode { get; set; }
    public string? IdentityCardNumberMasked { get; set; }
    public string? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Address { get; set; }
    public string? BloodType { get; set; }
    public string Role { get; set; } = "Patient";
    public bool IsActive { get; set; } = true;
    public string? AvatarUrl { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>
/// Generic API response wrapper conforming to enterprise standard format.
/// </summary>
/// <typeparam name="T">Payload data type.</typeparam>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string MessageCode { get; set; } = "MSG-SUC-01";
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string message = "Request processed successfully.")
    {
        return new ApiResponse<T>
        {
            Success = true,
            MessageCode = "MSG-SUC-01",
            Message = message,
            Data = data
        };
    }

    public static ApiResponse<T> Fail(string message, string messageCode = "MSG-ERR-01")
    {
        return new ApiResponse<T>
        {
            Success = false,
            MessageCode = messageCode,
            Message = message,
            Data = default
        };
    }
}
