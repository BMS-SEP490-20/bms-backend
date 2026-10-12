using BMS.Core.Entities;

namespace BMS.Core.Interfaces;

/// <summary>
/// Giao diện Repository quản lý dữ liệu mã xác thực OTP trong MongoDB.
/// Đảm bảo nguyên lý Dependency Inversion trong Clean Architecture.
/// </summary>
public interface IOtpRepository
{
    /// <summary>
    /// Lưu bản ghi mã OTP mới vào cơ sở dữ liệu.
    /// </summary>
    Task CreateAsync(OtpTokenEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Lấy bản ghi OTP mới nhất chưa sử dụng và chưa hết hạn của người nhận theo mục đích.
    /// </summary>
    Task<OtpTokenEntity?> GetLatestActiveByRecipientAsync(string recipient, string purpose, CancellationToken ct = default);

    /// <summary>
    /// Cập nhật trạng thái của bản ghi OTP (tăng số lần thử, đánh dấu đã sử dụng).
    /// </summary>
    Task UpdateAsync(OtpTokenEntity entity, CancellationToken ct = default);

    /// <summary>
    /// Vô hiệu hóa (hủy bỏ) tất cả các mã OTP còn hiệu lực trước đó của người nhận để đảm bảo mỗi thời điểm chỉ có 1 mã hợp lệ.
    /// </summary>
    Task InvalidatePreviousOtpsAsync(string recipient, string purpose, CancellationToken ct = default);

    /// <summary>
    /// Đếm số lần yêu cầu gửi OTP trong khoảng thời gian nhất định (Rate Limiting).
    /// </summary>
    Task<int> CountRecentRequestsAsync(string recipient, TimeSpan window, CancellationToken ct = default);
}
