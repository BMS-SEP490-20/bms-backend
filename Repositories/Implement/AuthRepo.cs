using iam_service.Data;
using iam_service.Modals;
using iam_service.Modals.Dto;
using iam_service.Repositories.Interface;
using iam_service.Service.Interface;
using IamService.Services.Interface;
using Microsoft.EntityFrameworkCore;



namespace iam_service.Repositories.Implement
{
    public class AuthRepo : IAuthRepo
    {
        private readonly UserManagementDBContext _context;
        private readonly IPasswordHasherService _hasherService;
        private readonly IJwtService _jwtService;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;

        public AuthRepo(
            UserManagementDBContext context,
            IPasswordHasherService hasherService,
            IJwtService jwtService,
            IOtpService otpService,
            IEmailService email
            )
        {
            _context = context;
            _hasherService = hasherService;
            _jwtService = jwtService;
            _otpService = otpService;
            _emailService = email;

        }

        public async Task<Account?> VerifyLoginAsync(string username, string password)
        {
            var user = await _context.Account.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null) return null;
            if (!_hasherService.VerifyPassword(user.Password, password))
                return null;
            return user;
        }

        public async Task<Account> RegisterAccountAsync(Account newAccount)
        {
            await _context.Account.AddAsync(newAccount);
            await _context.SaveChangesAsync();
            return newAccount;
        }

        public async Task<bool> IsEmailExistAsync(string email)
        {
            return await _context.Account.AnyAsync(u => u.Email == email);
        }

        public async Task UpdateLastLoginAsync(Account user)
        {
            user.LastLoginAt = DateTime.UtcNow;
            _context.Account.Update(user);
            await _context.SaveChangesAsync();
        }

        public async Task<Account?> GetByEmailAsync(string email)
        {
            return await _context.Account.FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task UpdatePasswordAsync(Account user)
        {
            var trackedUser = await _context.Account.FirstOrDefaultAsync(u => u.Id == user.Id);
            if (trackedUser == null)
                throw new InvalidOperationException("Tài khoản không tồn tại.");
            trackedUser.Password = user.Password;
            await _context.SaveChangesAsync();
        }

        public async Task<LoginResponseDto?> LoginAsync(string username, string password)
        {
            var user = await VerifyLoginAsync(username, password);
            if (user == null)
                return null;

            await UpdateLastLoginAsync(user);

            var roles = new List<string> { user.RoleId.ToString() };
            var token = _jwtService.GenerateToken(user, roles);

            return new LoginResponseDto { Token = token };
        }

        public async Task<string?> QuickRegisterAsync(string email)
        {
            if (await IsEmailExistAsync(email))
                return null;

            string baseUsername = email.Split('@')[0].Replace(".", "").ToLower();
            string generatedUsername = baseUsername + Guid.NewGuid().ToString().Substring(0, 4);

            string password = Guid.NewGuid().ToString("N").Substring(0, 5);
            string hashedPassword = _hasherService.HashPassword(password);

            var newAccount = new Account
            {
                Id = "ACC-" + Guid.NewGuid().ToString("N").Substring(0, 4),
                Username = generatedUsername,
                Password = hashedPassword,
                Email = email,
                CreatedAt = DateTime.UtcNow,
                RoleId = 5,
                Status = 1
            };

            await RegisterAccountAsync(newAccount);



            string subject = "Thông tin tài khoản đăng ký mới";
            string body = $"Bạn đã đăng ký thành công.<br/>Tên đăng nhập: <b>{generatedUsername}</b><br/>Mật khẩu: <b>{password}</b>";
            await _emailService.SendEmailAsync(email, subject, body);

            return $"Đăng ký thành công. Tên đăng nhập: {generatedUsername}";
        }

        public async Task<bool> SendResetPasswordOtpAsync(string email)
        {
            var isExist = await IsEmailExistAsync(email);
            if (!isExist) return false;
            return await _otpService.GenerateAndSendOtpAsync(email);
        }
        public async Task<bool> SendOtpAsync(string email)
        {

            return await _otpService.GenerateAndSendOtpAsync(email);
        }


        public async Task<bool> Verifyotp(VerifyOtpDto verifyOtp)
        {
            var isValidOtp = await _otpService.VerifyOtpAsync(verifyOtp.Email, verifyOtp.OtpCode);
            if (!isValidOtp) return false;


            return true;
        }
        public async Task<bool> ResetPasswordAsync(ResetPasswordSimple reset)
        {
            var isValidOtp = await _otpService.VerifyOtpAsync(reset.Email, reset.OtpCode);
            if (!isValidOtp) return false;

            var user = await GetByEmailAsync(reset.Email);
            if (user == null) return false;

            user.Password = _hasherService.HashPassword(reset.NewPassword);
            await UpdatePasswordAsync(user);
            return true;
        }
    }
}
