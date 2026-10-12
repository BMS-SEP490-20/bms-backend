using iam_service.Service.Interface;
using iam_service.Data;
using iam_service.Modals;
using System;
using System.Linq; // Cần thêm namespace này cho Where/OrderByDescending
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace iam_service.Service.Implement
{
    public class OtpService : IOtpService
    {
        private readonly UserManagementDBContext _context;
        private readonly IEmailService _emailService;
        private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);

        public OtpService(UserManagementDBContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // Sinh OTP, lưu vào database và gửi email
        public async Task<bool> GenerateAndSendOtpAsync(string email)
        {
            // 1. Cải tiến Bảo mật/Ngẫu nhiên: Sử dụng Random.Shared (.NET 6+)
            // để đảm bảo tính ngẫu nhiên tốt hơn trong môi trường đa luồng.
            var otpCode = Random.Shared.Next(100000, 999999).ToString();
            var now = DateTime.UtcNow;

            // 2. Tối ưu hóa Hiệu suất và Sửa Logic: Vô hiệu hóa TẤT CẢ OTP cũ chưa sử dụng (Status == 0).
            // Sử dụng ExecuteUpdateAsync (EF Core 7+) để cập nhật hàng loạt trực tiếp trong DB 
            // mà không cần tải dữ liệu vào bộ nhớ.

            // LƯU Ý: Nếu bạn dùng EF Core < 7, hãy thay thế khối này bằng cách dùng ToListAsync() 
            // và lặp lại như code ban đầu, nhưng sửa điều kiện Where.

            try
            {
                // Vô hiệu hóa các OTP đang hoạt động trước đó cho email này
                var oldOtps = await _context.OtpCodes
                    .Where(o => o.Email == email && o.Status == 0)
                    .ToListAsync();
                foreach (var oldOtp in oldOtps)
                {
                    oldOtp.Status = 2; // 2: Đã bị thay thế/Hết hạn
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Xử lý lỗi DB khi vô hiệu hóa OTP cũ (nếu cần)
                // Thông thường chỉ cần ghi log và tiếp tục
                Console.WriteLine($"Error disabling old OTPs: {ex.Message}");
            }
            //var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            // Tạo và lưu OTP mới
            var otpEntity = new OtpCodes
            {
                Email = email,
                OtpCode = otpCode,
                CreatedAt = now,
                ExpiresAt = now.Add(OtpLifetime),
                Status = 0 // 0: chưa sử dụng
            };

            await _context.OtpCodes.AddAsync(otpEntity);
            await _context.SaveChangesAsync();

            // Gửi email
            var subject = "Mã xác thực OTP của bạn";
            var body = $"Mã OTP của bạn là: <b>{otpCode}</b>. Mã này có hiệu lực trong 5 phút.";

            try
            {
                await _emailService.SendEmailAsync(email, subject, body);
                return true;
            }
            catch (Exception ex)
            {
                // Nếu gửi email thất bại, xóa OTP vừa tạo (Transactionality)
                _context.OtpCodes.Remove(otpEntity);
                await _context.SaveChangesAsync();

                // Ghi log lỗi gửi email
                Console.WriteLine($"Error sending OTP email: {ex.Message}");
                return false;
            }
        }

        // Kiểm tra OTP từ database (Giữ nguyên, vì nó đã tối ưu và chính xác)
        public async Task<bool> VerifyOtpAsync(string email, string otpCode)
        {
            var now = DateTime.UtcNow;
            var otp = await _context.OtpCodes
                .Where(o => o.Email == email && o.OtpCode == otpCode && o.Status == 0 && o.ExpiresAt > now)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefaultAsync();

            if (otp != null)
            {
                otp.Status = 1; // Đánh dấu đã xác thực
                await _context.SaveChangesAsync();
                return true;
            }

            return false;
        }
    }
}