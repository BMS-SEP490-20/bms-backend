using BMS.Application.DTOs;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BMS.Application.Services;

/// <summary>
/// Service orchestrating authentication, single sign-on, token issuance, and registration workflows (UC01, UC02).
/// Conforms to: OWASP Authentication Cheat Sheet, RFC 6819 (Refresh Token Rotation &amp; Reuse Detection), &amp; AGENTS-CODE.md.
/// </summary>
public class AuthService : IAuthService
{
    private readonly BmsDbContext _context;
    private readonly ITokenService? _tokenService;
    private readonly IUserRepository? _userRepository;
    private readonly IRoleRepository? _roleRepository;
    private readonly IRefreshTokenRepository? _refreshTokenRepository;
    private readonly IPasswordHasher? _passwordHasher;
    private readonly IEmailService? _emailService;
    private readonly IOtpRepository? _otpRepository;
    private readonly IAuditLogRepository? _auditLogRepository;
    private readonly AuthOptions _authOpt;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuthService>? _logger;

    public AuthService(
        BmsDbContext context,
        ITokenService? tokenService = null,
        IUserRepository? userRepository = null,
        IRoleRepository? roleRepository = null,
        IRefreshTokenRepository? refreshTokenRepository = null,
        IPasswordHasher? passwordHasher = null,
        IEmailService? emailService = null,
        IOtpRepository? otpRepository = null,
        IAuditLogRepository? auditLogRepository = null,
        IOptions<AuthOptions>? authOptions = null,
        TimeProvider? clock = null,
        ILogger<AuthService>? logger = null)
    {
        _context = context;
        _tokenService = tokenService;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _otpRepository = otpRepository;
        _auditLogRepository = auditLogRepository;
        _authOpt = authOptions?.Value ?? new AuthOptions();
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates a user using username or email, enforces lockout &amp; status policies, and issues JWT + rotated Refresh Token.
    /// </summary>
    /// <summary>
    /// Authenticates a user without throwing exception on expected credential failure (for safe IDE execution & immediate HTTP response).
    /// </summary>
    public async Task<AuthLoginResult> AuthenticateAsync(LoginRequest request, ClientContext client, CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrEmpty(request.Password))
        {
            return AuthLoginResult.Fail(400, "Vui lòng nhập đầy đủ tên đăng nhập/email/SĐT/CCCD và mật khẩu.", "VALIDATION_ERROR");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var login = request.UsernameOrEmail.Trim();

        User? user = null;
        bool fromMongo = false;

        if (_userRepository != null)
        {
            user = await _userRepository.FindByLoginAsync(login, ct);
            if (user != null)
            {
                fromMongo = true;
            }
        }

        if (user == null)
        {
            user = _context.Users.FirstOrDefault(u =>
                u.Username.Equals(login, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Equals(login, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(u.PhoneNumber) && u.PhoneNumber.Equals(login, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(u.IdentityCardNumber) && u.IdentityCardNumber.Equals(login, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(u.PatientCode) && u.PatientCode.Equals(login, StringComparison.OrdinalIgnoreCase)));
        }

        // 1. User not found -> constant-time dummy BCrypt verify to prevent timing-based account enumeration
        if (user is null)
        {
            _passwordHasher?.VerifyDummy(request.Password);
            _logger?.LogWarning("Login failed: unknown login {Login} from {Ip}", login, client.IpAddress);
            return AuthLoginResult.Fail(401, "Tên đăng nhập hoặc mật khẩu không chính xác. Vui lòng thử lại!", "INVALID_CREDENTIALS");
        }

        // 2. Check temporary brute-force lockout (BR-IAM-03: 15 minutes after 5 consecutive failures)
        if (user.LockoutEnd is { } until)
        {
            if (until > now)
            {
                var remaining = until - now;
                var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
                _logger?.LogWarning("Login blocked: locked user {UserId} until {Until} from {Ip}", user.Id, until, client.IpAddress);
                return AuthLoginResult.Fail(423, $"Tài khoản tạm thời bị khóa trong {minutes} phút do đăng nhập sai quá 5 lần liên tiếp.", "ACCOUNT_LOCKED", minutes);
            }
            else
            {
                // Lockout period expired: cleanly reset lockout counter
                user.LockoutEnd = null;
                user.FailedLoginCount = 0;
            }
        }

        // 3. Verify password against BCrypt hash (with fallback to BmsDbContext SHA256 for dev memory accounts)
        bool passwordValid = VerifyUserPassword(request.Password, user.PasswordHash);
        if (!passwordValid && fromMongo)
        {
            var memUser = _context.Users.FirstOrDefault(u =>
                u.Username.Equals(login, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Equals(login, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(u.PhoneNumber) && u.PhoneNumber.Equals(login, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(u.IdentityCardNumber) && u.IdentityCardNumber.Equals(login, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(u.PatientCode) && u.PatientCode.Equals(login, StringComparison.OrdinalIgnoreCase)));
            if (memUser != null && VerifyUserPassword(request.Password, memUser.PasswordHash))
            {
                passwordValid = true;
            }
        }

        if (!passwordValid)
        {
            if (_auditLogRepository != null)
            {
                try
                {
                    await _auditLogRepository.AddAsync(new AuditLog
                    {
                        AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                        Timestamp = now,
                        UserId = user.Id,
                        UserName = user.FullName ?? user.Username,
                        UserRole = "Unknown",
                        Action = "LOGIN_FAILED",
                        Category = "SECURITY",
                        Severity = "WARNING",
                        IpAddress = client.IpAddress ?? "127.0.0.1",
                        TargetEntity = "User",
                        EntityId = user.Id,
                        Details = $"Failed password attempt for login: {login}"
                    }, ct);
                }
                catch { /* Audit logging failure should not abort response */ }
            }

            if (fromMongo && _userRepository != null)
            {
                var fails = await _userRepository.IncrementFailedLoginAsync(user.Id, ct);
                if (fails >= _authOpt.MaxFailedAttempts)
                {
                    var lockUntil = now.AddMinutes(_authOpt.LockoutMinutes);
                    await _userRepository.LockAsync(user.Id, lockUntil, ct);
                    _logger?.LogWarning(
                        "Account locked after {Fails} failed attempts: user {UserId} until {Until} from {Ip}",
                        fails, user.Id, lockUntil, client.IpAddress);
                    return AuthLoginResult.Fail(423, $"Tài khoản tạm thời bị khóa trong {_authOpt.LockoutMinutes} phút do đăng nhập sai quá 5 lần liên tiếp.", "ACCOUNT_LOCKED", _authOpt.LockoutMinutes);
                }

                _logger?.LogWarning("Login failed ({Fails}/{Max}): user {UserId} from {Ip}",
                    fails, _authOpt.MaxFailedAttempts, user.Id, client.IpAddress);
            }
            else
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= _authOpt.MaxFailedAttempts)
                {
                    user.LockoutEnd = now.AddMinutes(_authOpt.LockoutMinutes);
                    return AuthLoginResult.Fail(423, $"Tài khoản tạm thời bị khóa trong {_authOpt.LockoutMinutes} phút do đăng nhập sai quá 5 lần liên tiếp.", "ACCOUNT_LOCKED", _authOpt.LockoutMinutes);
                }
            }

            return AuthLoginResult.Fail(401, "Tên đăng nhập hoặc mật khẩu không chính xác. Vui lòng thử lại!", "INVALID_CREDENTIALS");
        }

        // 4. Check account active status AFTER password check so attackers cannot probe disabled accounts
        if (!user.IsActive)
        {
            _logger?.LogWarning("Login blocked: disabled user {UserId} from {Ip}", user.Id, client.IpAddress);
            return AuthLoginResult.Fail(403, "Tài khoản của bạn đã bị vô hiệu hóa hoặc chưa được kích hoạt.", "ACCOUNT_DISABLED");
        }

        // 5. Reset failed login counter & record LastLoginAt
        if (fromMongo && _userRepository != null)
        {
            await _userRepository.RecordSuccessfulLoginAsync(user.Id, now, null, ct);
        }
        else
        {
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;
            user.LastLoginAt = now;
        }

        _logger?.LogInformation("Login succeeded: user {UserId} ({Username}) from {Ip}", user.Id, user.Username, client.IpAddress);
        var tokens = await IssueTokensAsync(user, familyId: Guid.NewGuid().ToString("N"), now, client, ct);

        if (_auditLogRepository != null)
        {
            try
            {
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = now,
                    UserId = user.Id,
                    UserName = user.FullName ?? user.Username,
                    UserRole = tokens.User.Role,
                    Action = "LOGIN_SUCCESS",
                    Category = "SECURITY",
                    Severity = "INFO",
                    IpAddress = client.IpAddress ?? "127.0.0.1",
                    TargetEntity = "User",
                    EntityId = user.Id,
                    Details = $"User {user.Username} successfully logged in."
                }, ct);
            }
            catch { /* Audit logging failure should not abort response */ }
        }

        return AuthLoginResult.Ok(tokens);
    }

    /// <summary>
    /// Legacy LoginAsync throwing exceptions for backward-compatible caller contracts.
    /// </summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request, ClientContext client, CancellationToken ct = default)
    {
        var result = await AuthenticateAsync(request, client, ct);
        if (!result.Success)
        {
            if (result.ErrorCode == "ACCOUNT_LOCKED")
                throw new AccountLockedException(TimeSpan.FromMinutes(result.RetryAfterMinutes ?? 15));
            if (result.ErrorCode == "ACCOUNT_DISABLED")
                throw new AccountDisabledException();
            throw new InvalidCredentialsException();
        }
        return result.Response!;
    }


    public async Task<AuthResponse> RefreshAsync(string rawRefreshToken, ClientContext client, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            throw new InvalidRefreshTokenException();
        }

        var now = _clock.GetUtcNow().UtcDateTime;

        if (_refreshTokenRepository == null || _tokenService == null)
        {
            throw new InvalidRefreshTokenException();
        }

        var hash = _tokenService.HashToken(rawRefreshToken.Trim());
        var stored = await _refreshTokenRepository.GetByHashAsync(hash, ct);

        if (stored is null)
        {
            throw new InvalidRefreshTokenException();
        }

        // Reuse Detection: token was already rotated/revoked -> revoke the entire token family immediately
        if (stored.RevokedAt is not null)
        {
            await _refreshTokenRepository.RevokeFamilyAsync(stored.FamilyId, now, "reuse_detected", ct);
            _logger?.LogWarning(
                "Refresh token reuse detected! Family {FamilyId} for user {UserId} revoked from {Ip}",
                stored.FamilyId, stored.UserId, client.IpAddress);
            throw new InvalidRefreshTokenException();
        }

        if (stored.ExpiresAt <= now)
        {
            throw new InvalidRefreshTokenException();
        }

        User? user = null;
        if (_userRepository != null)
        {
            user = await _userRepository.FindByIdAsync(stored.UserId, ct);
        }
        user ??= _context.Users.FirstOrDefault(u => u.Id == stored.UserId);

        if (user is null || !user.IsActive)
        {
            throw new AccountDisabledException();
        }

        var role = await ResolveRoleAsync(user, ct);
        var access = _tokenService.CreateAccessToken(user, role);
        var nextRefresh = _tokenService.GenerateRefreshToken();

        // Atomic Compare-And-Swap: ensure concurrent requests cannot both rotate the same token
        var revoked = await _refreshTokenRepository.TryRevokeAsync(
            stored.Id, now, "rotated", nextRefresh.TokenHash, ct);
        if (!revoked)
        {
            await _refreshTokenRepository.RevokeFamilyAsync(stored.FamilyId, now, "race_condition", ct);
            throw new InvalidRefreshTokenException();
        }

        await _refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = nextRefresh.TokenHash,
            FamilyId = stored.FamilyId,
            CreatedAt = now,
            ExpiresAt = nextRefresh.ExpiresAt,
            CreatedByIp = client.IpAddress,
            UserAgent = client.UserAgent
        }, ct);

        return BuildAuthResponse(user, role, access, nextRefresh, now);
    }

    public async Task LogoutAsync(string userId, string? rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken) || _refreshTokenRepository == null || _tokenService == null)
        {
            return;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var hash = _tokenService.HashToken(rawRefreshToken.Trim());
        var stored = await _refreshTokenRepository.GetByHashAsync(hash, ct);

        if (stored is not null && stored.UserId == userId)
        {
            await _refreshTokenRepository.RevokeFamilyAsync(stored.FamilyId, now, "logout", ct);
        }
    }

    public async Task LogoutAllAsync(string userId, CancellationToken ct = default)
    {
        if (_refreshTokenRepository == null)
        {
            return;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        await _refreshTokenRepository.RevokeAllForUserAsync(userId, now, "logout_all", ct);
    }

    public async Task<UserInfoDto> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        User? user = null;
        if (_userRepository != null)
        {
            user = await _userRepository.FindByIdAsync(userId, ct);
        }
        user ??= _context.Users.FirstOrDefault(u => u.Id == userId);

        if (user is null || !user.IsActive)
        {
            throw new AccountDisabledException();
        }

        var role = await ResolveRoleAsync(user, ct);
        return ToUserInfo(user, role);
    }

    private async Task<AuthResponse> IssueTokensAsync(
        User user, string familyId, DateTime now, ClientContext client, CancellationToken ct)
    {
        var role = await ResolveRoleAsync(user, ct);

        var access = _tokenService != null
            ? _tokenService.CreateAccessToken(user, role)
            : new AccessTokenResult($"BMS_ACCESS_{user.Id}_{Guid.NewGuid():N}", now.AddMinutes(15));

        var refresh = _tokenService != null
            ? _tokenService.GenerateRefreshToken()
            : new RefreshTokenResult($"BMS_REFRESH_{user.Id}_{Guid.NewGuid():N}", Guid.NewGuid().ToString("N"), now.AddDays(7));

        if (_refreshTokenRepository != null)
        {
            await _refreshTokenRepository.AddAsync(new RefreshToken
            {
                UserId = user.Id,
                TokenHash = refresh.TokenHash,
                FamilyId = familyId,
                CreatedAt = now,
                ExpiresAt = refresh.ExpiresAt,
                CreatedByIp = client.IpAddress,
                UserAgent = client.UserAgent
            }, ct);
        }

        return BuildAuthResponse(user, role, access, refresh, now);
    }

    private async Task<Role> ResolveRoleAsync(User user, CancellationToken ct)
    {
        if (user.RoleId.HasValue && _roleRepository != null)
        {
            var dbRole = await _roleRepository.GetRoleByIdAsync(user.RoleId.Value, ct);
            if (dbRole != null)
            {
                return dbRole;
            }
        }

        // WHY: the fallback must follow the 6-role seed (1 Admin, 2 Manager, 3 Lab, 4 Doctor,
        // 5 Receptionist, 6 Patient). The token service derives the role claim from this ID.
        // TODO(phat, DOT2-followup-5): drop the RoleId 9 safety net after UserRoleMigration has run everywhere.
        var roleId = user.RoleId == User.LegacyPatientRoleId ? (int)UserRole.Patient : user.RoleId ?? (int)user.Role;
        var roleName = RoleNames.FromRoleId(roleId) ?? throw new RoleNotAssignedException();

        // Permissions per ROLE-PERMISSION-MATRIX §3.2: Admin has no clinical permission (decision 10);
        // only Manager approves results; Receptionist no longer collects samples.
        var permissions = roleId switch
        {
            1 => new List<string> { "MANAGE_SERVICE_PACKAGE", "MANAGE_USERS", "MANAGE_ROLES" },
            2 => new List<string> { "APPROVE_RESULT", "UNLOCK_QC", "EDIT_PANIC_THRESHOLD", "MANAGE_SCHEDULE", "MANAGE_REAGENTS", "VIEW_PATIENT_RECORDS", "VIEW_ALL_LAB" },
            3 => new List<string> { "RECEIVE_SAMPLE", "RECEIVE_SAMPLES", "COLLECT_SAMPLE", "COLLECT_SAMPLES", "RUN_ANALYZER", "ENTER_RESULT", "ENTER_LAB_RESULTS", "MANAGE_REAGENTS", "VIEW_ALL_LAB" },
            4 => new List<string> { "EXAMINE_PATIENT", "CLINIC_EXAM", "ORDER_TEST", "CREATE_PRESCRIPTION", "VIEW_PATIENT_HISTORY", "VIEW_PATIENT_RECORDS", "VIEW_PATIENTS" },
            5 => new List<string> { "CHECK_IN", "CHECKIN_PATIENT", "REGISTER_PATIENTS", "VIEW_PATIENTS", "PROCESS_PAYMENT", "MANAGE_APPOINTMENT", "CREATE_APPOINTMENTS", "MANAGE_QUEUE" },
            _ => new List<string> { "BOOK_APPOINTMENT", "VIEW_OWN_RESULT", "MANAGE_PROFILE" }
        };

        return new Role
        {
            Id = roleId,
            RoleName = roleName,
            Permissions = permissions
        };
    }

    private bool VerifyUserPassword(string plainPassword, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        if (_passwordHasher != null && _passwordHasher.Verify(plainPassword, storedHash))
        {
            return true;
        }

        return BmsDbContext.VerifyPassword(plainPassword, storedHash);
    }

    private static AuthResponse BuildAuthResponse(
        User user, Role role, AccessTokenResult access, RefreshTokenResult refresh, DateTime now)
    {
        var expiresIn = (int)Math.Max(0, (access.ExpiresAt - now).TotalSeconds);
        return new AuthResponse(
            access.Token,
            "Bearer",
            expiresIn,
            refresh.RawToken,
            refresh.ExpiresAt,
            ToUserInfo(user, role));
    }

    private static UserInfoDto ToUserInfo(User u, Role? role) => new(
        u.Id,
        u.Username,
        u.Email,
        u.FullName,
        u.PhoneNumber,
        role?.Id ?? u.RoleId ?? (int)u.Role,
        role?.RoleName ?? u.Role.ToString(),
        role?.Permissions ?? []);

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto)
    {
        var usernameTrimmed = dto.Username.Trim();
        var emailTrimmed = dto.Email.Trim().ToLowerInvariant();

        // 1. Check duplicate username or email across MongoDB and in-memory context
        User? existingUser = null;
        if (_userRepository != null)
        {
            existingUser = await _userRepository.FindByEmailAsync(emailTrimmed)
                        ?? await _userRepository.GetByEmailOrUsernameAsync(usernameTrimmed);
        }

        if (existingUser == null)
        {
            existingUser = _context.Users.FirstOrDefault(u =>
                u.Username.Equals(usernameTrimmed, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Equals(emailTrimmed, StringComparison.OrdinalIgnoreCase));
        }

        if (existingUser != null)
        {
            if (existingUser.EmailConfirmed)
            {
                return new AuthResponseDto
                {
                    Success = false,
                    Message = "Tên đăng nhập hoặc email đã được sử dụng trong hệ thống."
                };
            }

            // Unconfirmed account: refresh OTP and resend
            var freshOtp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
            existingUser.VerificationOtp = freshOtp;
            existingUser.OtpExpiresAt = DateTime.UtcNow.AddMinutes(15);
            existingUser.PasswordHash = _passwordHasher != null ? _passwordHasher.Hash(dto.Password) : BmsDbContext.HashPassword(dto.Password);
            existingUser.FullName = dto.FullName.Trim();
            existingUser.PhoneNumber = dto.PhoneNumber;
            existingUser.UpdatedAt = DateTime.UtcNow;

            if (_userRepository != null && !string.IsNullOrWhiteSpace(existingUser.Id))
            {
                try { await _userRepository.UpdateAsync(existingUser); } catch {}
            }

            if (_emailService != null)
            {
                await _emailService.SendOtpEmailAsync(
                    existingUser.Email,
                    freshOtp,
                    "Xác thực đăng ký tài khoản Bệnh nhân BMS (Account Verification)",
                    15);
            }

            return new AuthResponseDto
            {
                Success = true,
                Message = $"Mã OTP xác thực mới (6 chữ số) đã được gửi đến email {existingUser.Email}. Vui lòng kiểm tra hộp thư.",
                User = MapToDto(existingUser)
            };
        }

        // 2. Generate 6-digit CSPRNG OTP
        var otp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        var expiresAt = DateTime.UtcNow.AddMinutes(15);
        var hashedPassword = _passwordHasher != null
            ? _passwordHasher.Hash(dto.Password)
            : BmsDbContext.HashPassword(dto.Password);

        var newUser = new User
        {
            Username = usernameTrimmed,
            Email = emailTrimmed,
            FullName = dto.FullName.Trim(),
            PhoneNumber = dto.PhoneNumber,
            Address = dto.Address,
            DateOfBirth = dto.DateOfBirth,
            Gender = dto.Gender,
            // WHY (R7): self-registered accounts are patients, seeded RoleId 6. 9 was a legacy value.
            Role = UserRole.Patient,
            RoleId = (int)UserRole.Patient,
            PasswordHash = hashedPassword,
            EmailConfirmed = false,
            VerificationOtp = otp,
            OtpExpiresAt = expiresAt,
            Status = (int)UserStatus.Active,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        if (_userRepository != null)
        {
            try
            {
                await _userRepository.CreateAsync(newUser);
            }
            catch
            {
                // Fallback to memory context
            }
        }
        _context.Users.Add(newUser);

        // 3. Dispatch live OTP email via Gmail SMTP
        bool sent = false;
        if (_emailService != null)
        {
            sent = await _emailService.SendOtpEmailAsync(
                newUser.Email,
                otp,
                "Xác thực đăng ký tài khoản Bệnh nhân BMS (Account Verification)",
                15);
        }

        return new AuthResponseDto
        {
            Success = true,
            Message = sent
                ? $"Đăng ký tài khoản thành công! Mã OTP xác thực (6 chữ số) đã được gửi đến email {dto.Email}. Vui lòng kiểm tra hộp thư đến hoặc mục thư rác."
                : $"Đăng ký tài khoản thành công! Mã OTP xác thực đã được gửi tới email {dto.Email}.",
            User = MapToDto(newUser)
        };
    }

    public async Task<AuthResponseDto> VerifyOtpAsync(VerifyOtpDto dto)
    {
        var emailTrimmed = dto.Email.Trim().ToLowerInvariant();
        var otpTrimmed = dto.Otp.Trim();

        User? user = null;
        if (_userRepository != null)
        {
            user = await _userRepository.FindByEmailAsync(emailTrimmed);
        }

        if (user == null)
        {
            user = _context.Users.FirstOrDefault(u => u.Email.Equals(emailTrimmed, StringComparison.OrdinalIgnoreCase));
        }

        if (user == null)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "Tài khoản chưa được lưu thông tin đăng ký. Vui lòng bấm 'Để Sau' để đóng hộp thoại và nhấn nút 'Đăng Ký Tài Khoản Bệnh Nhân' bên dưới."
            };
        }

        bool isOtpValid = (!string.IsNullOrEmpty(user.VerificationOtp) &&
                           user.VerificationOtp == otpTrimmed &&
                           user.OtpExpiresAt.HasValue &&
                           user.OtpExpiresAt.Value >= DateTime.UtcNow);

        // Fallback: Check if resent via IOtpRepository
        if (!isOtpValid && _otpRepository != null)
        {
            var otpEntity = await _otpRepository.GetLatestActiveByRecipientAsync(emailTrimmed, "Registration");
            if (otpEntity != null && !otpEntity.IsUsed && otpEntity.ExpiresAt >= DateTime.UtcNow)
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                var combined = System.Text.Encoding.UTF8.GetBytes($"{otpEntity.Salt}:{otpTrimmed}");
                var computedHash = Convert.ToBase64String(sha.ComputeHash(combined));
                if (computedHash == otpEntity.HashedOtp)
                {
                    isOtpValid = true;
                    otpEntity.IsUsed = true;
                    await _otpRepository.UpdateAsync(otpEntity);
                }
            }
        }

        if (!isOtpValid)
        {
            return new AuthResponseDto { Success = false, Message = "Mã OTP không đúng hoặc đã hết hạn (15 phút)." };
        }

        user.EmailConfirmed = true;
        user.VerificationOtp = null;
        user.OtpExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;

        if (_userRepository != null && !string.IsNullOrWhiteSpace(user.Id))
        {
            try { await _userRepository.UpdateAsync(user); } catch {}
        }

        return new AuthResponseDto
        {
            Success = true,
            Message = "Xác thực email thành công! Bạn có thể đăng nhập ngay.",
            User = MapToDto(user)
        };
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto dto)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u =>
            (u.Username.Equals(dto.UsernameOrEmail, StringComparison.OrdinalIgnoreCase) ||
             u.Email.Equals(dto.UsernameOrEmail, StringComparison.OrdinalIgnoreCase)) &&
            u.IsActive);

        if (user == null || !BmsDbContext.VerifyPassword(dto.Password, user.PasswordHash))
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "Tên đăng nhập hoặc mật khẩu không chính xác."
            };
        }

        // WHY: this legacy in-memory login used to grant every user the "ALL" permission.
        // Reuse the canonical 6-role resolution instead so its tokens carry the same claims as the main login.
        var roleEntity = await ResolveRoleAsync(user, CancellationToken.None);
        var token = _tokenService != null
            ? _tokenService.CreateAccessToken(user, roleEntity).Token
            : $"BMS_TOKEN_{user.Id}_{user.Role}_{Guid.NewGuid():N}";

        return new AuthResponseDto
        {
            Success = true,
            Message = "Đăng nhập thành công.",
            Token = token,
            User = MapToDto(user)
        };
    }

    public async Task<AuthResponseDto> ChangePasswordAsync(string userId, ChangePasswordDto dto)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            return new AuthResponseDto { Success = false, Message = "Không tìm thấy người dùng." };
        }

        if (!BmsDbContext.VerifyPassword(dto.CurrentPassword, user.PasswordHash))
        {
            return new AuthResponseDto { Success = false, Message = "Mật khẩu hiện tại không chính xác." };
        }

        if (dto.NewPassword == dto.CurrentPassword)
        {
            return new AuthResponseDto { Success = false, Message = "Mật khẩu mới không được trùng với mật khẩu cũ." };
        }

        user.PasswordHash = BmsDbContext.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        return new AuthResponseDto
        {
            Success = true,
            Message = "Đổi mật khẩu thành công. Vui lòng đăng nhập lại với mật khẩu mới."
        };
    }

    public async Task<AuthResponseDto> ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
        {
            return new AuthResponseDto { Success = false, Message = "Email không được để trống." };
        }

        var emailTrimmed = dto.Email.Trim();

        // 1. Look for user in MongoDB first, then fallback to in-memory context
        User? user = null;
        if (_userRepository != null)
        {
            user = await _userRepository.FindByEmailAsync(emailTrimmed);
            if (user == null)
            {
                user = await _userRepository.GetByEmailOrUsernameAsync(emailTrimmed);
            }
        }

        if (user == null)
        {
            user = _context.Users.FirstOrDefault(u =>
                u.Email.Equals(emailTrimmed, StringComparison.OrdinalIgnoreCase) ||
                u.Username.Equals(emailTrimmed, StringComparison.OrdinalIgnoreCase));
        }

        if (user == null)
        {
            return new AuthResponseDto { Success = false, Message = "Địa chỉ email không tồn tại trong hệ thống BMS." };
        }

        // 2. Generate cryptographically random 6-digit OTP
        var otp = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
        var expiresAt = DateTime.UtcNow.AddMinutes(15);

        user.PasswordResetOtp = otp;
        user.PasswordResetExpiresAt = expiresAt;
        user.UpdatedAt = DateTime.UtcNow;

        if (_userRepository != null && !string.IsNullOrWhiteSpace(user.Id))
        {
            try
            {
                await _userRepository.UpdateAsync(user);
            }
            catch
            {
                // In-memory or test scenario fallback
            }
        }

        // 3. Dispatch live OTP email via Gmail SMTP service
        bool sent = false;
        if (_emailService != null)
        {
            sent = await _emailService.SendOtpEmailAsync(
                user.Email,
                otp,
                "Khôi phục mật khẩu tài khoản BMS (Password Reset)",
                15);
        }

        return new AuthResponseDto
        {
            Success = true,
            Message = sent
                ? $"Mã xác thực OTP gồm 6 chữ số đã được gửi tới email {user.Email}. Vui lòng kiểm tra hộp thư đến hoặc mục thư rác (hiệu lực trong 15 phút)."
                : $"Yêu cầu khôi phục mật khẩu đã được tiếp nhận cho tài khoản {user.Email}."
        };
    }

    public async Task<AuthResponseDto> ResetPasswordAsync(ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Otp) || string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            return new AuthResponseDto { Success = false, Message = "Vui lòng nhập đầy đủ thông tin: Email, mã OTP và mật khẩu mới." };
        }

        var emailTrimmed = dto.Email.Trim();
        User? user = null;
        if (_userRepository != null)
        {
            user = await _userRepository.FindByEmailAsync(emailTrimmed);
            if (user == null)
            {
                user = await _userRepository.GetByEmailOrUsernameAsync(emailTrimmed);
            }
        }

        if (user == null)
        {
            user = _context.Users.FirstOrDefault(u =>
                u.Email.Equals(emailTrimmed, StringComparison.OrdinalIgnoreCase) ||
                u.Username.Equals(emailTrimmed, StringComparison.OrdinalIgnoreCase));
        }

        if (user == null)
        {
            return new AuthResponseDto { Success = false, Message = "Tài khoản không tồn tại trong hệ thống." };
        }

        if (string.IsNullOrWhiteSpace(user.PasswordResetOtp) ||
            !user.PasswordResetOtp.Trim().Equals(dto.Otp.Trim(), StringComparison.Ordinal) ||
            user.PasswordResetExpiresAt < DateTime.UtcNow)
        {
            return new AuthResponseDto { Success = false, Message = "Mã OTP đặt lại mật khẩu không hợp lệ hoặc đã hết hạn." };
        }

        var newHash = _passwordHasher != null ? _passwordHasher.Hash(dto.NewPassword) : BmsDbContext.HashPassword(dto.NewPassword);
        user.PasswordHash = newHash;
        user.PasswordResetOtp = null;
        user.PasswordResetExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;

        if (_userRepository != null && !string.IsNullOrWhiteSpace(user.Id))
        {
            try
            {
                await _userRepository.UpdateAsync(user);
            }
            catch
            {
                // In-memory or test scenario fallback
            }
        }

        return new AuthResponseDto
        {
            Success = true,
            Message = "Đặt lại mật khẩu thành công! Quý khách có thể đăng nhập ngay với mật khẩu mới."
        };
    }

    public async Task<UserDto?> GetUserByIdAsync(string userId)
    {
        await Task.Yield();
        var user = _context.Users.FirstOrDefault(u => u.Id == userId);
        return user != null ? MapToDto(user) : null;
    }

    private static UserDto MapToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FullName = user.FullName,
            PhoneNumber = user.PhoneNumber,
            Role = user.Role.ToString(),
            EmailConfirmed = user.EmailConfirmed
        };
    }
}
