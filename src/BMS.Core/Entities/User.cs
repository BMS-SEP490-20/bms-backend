namespace BMS.Core.Entities;

/// <summary>Trạng thái tài khoản (field Status trong MongoDB).</summary>
public enum UserStatus
{
    Active = 1
    // Mọi giá trị khác Active (0, 2, 3...) đều bị coi là không được phép đăng nhập.
}

/// <summary>Tài khoản đăng nhập – map với collection "accounts".</summary>
public class User
{
    public string Id { get; set; } = default!;
    public string Username { get; set; } = default!;

    /// <summary>BCrypt hash. TUYỆT ĐỐI không trả ra ngoài API.</summary>
    public string Password { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string FullName { get; set; } = default!;
    public string? PhoneNumber { get; set; }
    public int RoleId { get; set; }
    public int Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // --- Chống brute-force (field mới, document cũ không có sẽ mặc định 0/null) ---
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }

    public bool IsActive => Status == (int)UserStatus.Active;
}
