using System.ComponentModel.DataAnnotations;
using System.Text;

namespace BMS.Application.DTOs.Auth;

public sealed class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(254)]
    public string Email { get; init; } = default!;
}

public sealed class ResetPasswordRequest : IValidatableObject
{
    [Required(ErrorMessage = "Thiếu mã đặt lại mật khẩu.")]
    [StringLength(512, MinimumLength = 20, ErrorMessage = "Mã đặt lại mật khẩu không hợp lệ.")]
    public string Token { get; init; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
    [StringLength(64, MinimumLength = 8, ErrorMessage = "Mật khẩu phải từ 8 đến 64 ký tự.")]
    public string NewPassword { get; init; } = default!;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Xác nhận mật khẩu không khớp.")]
    public string ConfirmPassword { get; init; } = default!;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var p = NewPassword;
        if (string.IsNullOrEmpty(p)) yield break;

        if (!p.Any(char.IsLower) || !p.Any(char.IsUpper) || !p.Any(char.IsDigit))
            yield return new ValidationResult(
                "Mật khẩu phải có chữ hoa, chữ thường và chữ số.", [nameof(NewPassword)]);

        // BCrypt chỉ dùng 72 byte đầu
        if (Encoding.UTF8.GetByteCount(p) > 72)
            yield return new ValidationResult("Mật khẩu quá dài.", [nameof(NewPassword)]);
    }
}

public sealed record MessageResponse(string Message);
