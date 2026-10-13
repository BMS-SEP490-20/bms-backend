using IamService.Services.Interface;
using BCrypt.Net;

namespace IamService.Services.Implement
{
    // Sử dụng BCrypt để băm mật khẩu
    public class PasswordHasherService : IPasswordHasherService
    {
        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        public bool VerifyPassword(string hashedPassword, string password)
        {
            return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
        }
    }
}