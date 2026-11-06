using BMS.Application.DTOs;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using Google.Apis.Auth;
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
    private readonly IPatientRepository? _patientRepository;
    private readonly IAuditLogRepository? _auditLogRepository;
    private readonly Microsoft.Extensions.Configuration.IConfiguration? _configuration;
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
        IPatientRepository? patientRepository = null,
        IAuditLogRepository? auditLogRepository = null,
        IOptions<AuthOptions>? authOptions = null,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null,
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
        _patientRepository = patientRepository;
        _auditLogRepository = auditLogRepository;
        _configuration = configuration;
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
            return AuthLoginResult.Fail(400, "Vui lòng nhập đầy đủ tên đăng nhập hoặc email và mật khẩu.", "VALIDATION_ERROR");
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
                (u.PhoneNumber != null && u.PhoneNumber.Equals(login, StringComparison.OrdinalIgnoreCase)) ||
                (u.IdentityCardNumber != null && u.IdentityCardNumber.Equals(login, StringComparison.OrdinalIgnoreCase)));
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
                u.Email.Equals(login, StringComparison.OrdinalIgnoreCase));
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
        try
        {
            await TryEnsurePatientProfileAsync(user, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "PATIENT_IDENTITY_CONFLICT")
        {
            return AuthLoginResult.Fail(409, "Số CCCD/Định danh này đã được liên kết với một tài khoản khác trong hệ thống hoặc không khớp thông tin xác thực.", "PATIENT_IDENTITY_CONFLICT");
        }
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

    /// <summary>
    /// Authenticates or auto-registers a user using a verified Google OAuth 2.0 ID Token.
    /// Conforms to: OpenID Connect (OIDC) specifications & NIST SP 800-63B standards.
    /// </summary>
    public async Task<AuthLoginResult> GoogleLoginAsync(GoogleAuthRequestDto request, ClientContext client, CancellationToken ct = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.IdToken))
        {
            return AuthLoginResult.Fail(400, "Vui lòng cung cấp mã xác thực Google hợp lệ.", "VALIDATION_ERROR");
        }

        var configuredClientId = _configuration?["Google:ClientId"]
            ?? Environment.GetEnvironmentVariable("Google__ClientId");

        if (string.IsNullOrWhiteSpace(configuredClientId) || configuredClientId.StartsWith("YOUR_"))
        {
            _logger?.LogError("Google OAuth Client ID is not configured on the server. Please set Google__ClientId in .env.");
            return AuthLoginResult.Fail(500, "Dịch vụ đăng nhập Google chưa được cấu hình Client ID trên hệ thống máy chủ.", "GOOGLE_OAUTH_NOT_CONFIGURED");
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { configuredClientId.Trim() }
            };

            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken.Trim(), settings);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Google token validation failed: {Message}", ex.Message);
            return AuthLoginResult.Fail(400, "Xác thực mã Google thất bại hoặc mã đã hết hạn. Vui lòng đăng nhập lại.", "INVALID_GOOGLE_TOKEN");
        }

        if (string.IsNullOrWhiteSpace(payload?.Email))
        {
            return AuthLoginResult.Fail(400, "Không thể trích xuất địa chỉ email từ tài khoản Google của bạn.", "MISSING_GOOGLE_EMAIL");
        }

        if (!payload.EmailVerified)
        {
            return AuthLoginResult.Fail(400, "Địa chỉ email Google chưa được xác thực. Vui lòng xác thực email trên tài khoản Google trước.", "GOOGLE_EMAIL_NOT_VERIFIED");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var email = payload.Email.Trim().ToLowerInvariant();

        User? user = null;
        bool fromMongo = false;

        if (_userRepository != null)
        {
            user = await _userRepository.FindByEmailAsync(email);
            if (user != null) fromMongo = true;
        }

        user ??= _context.Users.FirstOrDefault(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase));
        bool isNewGoogleUser = (user == null);

        if (user != null)
        {
            // Existing user
            if (user.Status == (int)UserStatus.Inactive || user.Status == (int)UserStatus.Suspended)
            {
                return AuthLoginResult.Fail(403, "Tài khoản của bạn đã bị vô hiệu hóa hoặc tạm khóa. Vui lòng liên hệ ban quản trị.", "ACCOUNT_DISABLED");
            }

            if (user.Status == (int)UserStatus.Locked && user.LockoutEnd > now)
            {
                var remainingMinutes = (int)Math.Ceiling((user.LockoutEnd.Value - now).TotalMinutes);
                return AuthLoginResult.Fail(423, $"Tài khoản tạm thời bị khóa. Vui lòng thử lại sau {remainingMinutes} phút.", "ACCOUNT_LOCKED", remainingMinutes);
            }

            user.EmailConfirmed = true;
            if (string.IsNullOrWhiteSpace(user.GoogleId)) user.GoogleId = payload.Subject;
            if (string.IsNullOrWhiteSpace(user.AvatarUrl) && !string.IsNullOrWhiteSpace(payload.Picture)) user.AvatarUrl = payload.Picture;
            if (string.IsNullOrWhiteSpace(user.AuthProvider)) user.AuthProvider = "Google";
            user.LastLoginAt = now;
            user.FailedLoginCount = 0;
            user.LockoutEnd = null;

            if (fromMongo && _userRepository != null)
            {
                try { await _userRepository.UpdateAsync(user); } catch {}
            }
        }
        else
        {
            // Auto-register as Patient
            var baseUsername = email.Split('@')[0].Replace(".", "_");
            var username = baseUsername;
            int counter = 1;
            while ((_userRepository != null && await _userRepository.GetByEmailOrUsernameAsync(username) != null) ||
                   _context.Users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            {
                username = $"{baseUsername}_{counter++}";
            }

            var newUser = new User
            {
                Username = username,
                Email = email,
                FullName = !string.IsNullOrWhiteSpace(payload.Name) ? payload.Name.Trim() : username,
                RoleId = (int)UserRole.Patient,
                Role = UserRole.Patient,
                EmailConfirmed = true,
                GoogleId = payload.Subject,
                AuthProvider = "Google",
                AvatarUrl = payload.Picture,
                Status = (int)UserStatus.Active,
                IsActive = true,
                CreatedAt = now,
                LastLoginAt = now,
                PasswordHash = string.Empty
            };

            if (_userRepository != null)
            {
                try { await _userRepository.CreateAsync(newUser); fromMongo = true; } catch {}
            }
            _context.Users.Add(newUser);
            user = newUser;
        }

        // Tự động đồng bộ và tạo hồ sơ Bệnh nhân trong MongoDB patients collection (BR-REC-01)
        if (_patientRepository != null && user.RoleId == (int)UserRole.Patient)
        {
            Patient? existingPat = null;
            bool readSuccess = false;
            try
            {
                existingPat = await _patientRepository.GetByAccountIdAsync(user.Id, ct);
                readSuccess = true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Transient warning: could not verify existing patient profile for Google SSO user {UserId}", user.Id);
            }

            // Insert only when confirmed missing by successful read OR when newly auto-registered user
            if ((readSuccess && existingPat == null) || (isNewGoogleUser && existingPat == null))
            {
                var idNum = user.IdentityCardNumber?.Trim();
                var patId = !string.IsNullOrWhiteSpace(idNum) ? $"PAT-{idNum}" : $"PAT-{DateTime.UtcNow.Ticks.ToString()[^10..]}";
                var newPat = new Patient
                {
                    Id = patId,
                    AccountId = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    PhoneNumber = user.PhoneNumber ?? "",
                    IdentityCardNumber = idNum,
                    Address = user.Address ?? "",
                    DateOfBirth = user.DateOfBirth,
                    GenderString = NormalizeGender(user.Gender),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                try
                {
                    await _patientRepository.InsertAsync(newPat, ct);
                }
                catch (PatientDuplicateException ex)
                {
                    _logger?.LogInformation(ex, "Patient duplicate detected in MongoDB for Google SSO user {UserId}, attempting resolution", user.Id);
                    var (resolved, isConflict) = await ResolvePatientDuplicateAsync(user, idNum, patId, ct);
                    if (!resolved && isConflict)
                    {
                        return AuthLoginResult.Fail(409, "Số CCCD/Định danh này đã được liên kết với một tài khoản khác trong hệ thống hoặc không khớp thông tin xác thực. Vui lòng liên hệ bộ phận tiếp đón để được hỗ trợ.", "PATIENT_IDENTITY_CONFLICT");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to auto-create patient profile in MongoDB for Google SSO user {UserId}", user.Id);
                    if (isNewGoogleUser)
                    {
                        return AuthLoginResult.Fail(500, "Không thể khởi tạo hồ sơ bệnh nhân từ tài khoản Google. Vui lòng liên hệ quản trị viên hoặc thử lại sau.", "PATIENT_PROFILE_CREATION_FAILED");
                    }
                }
            }
        }

        try
        {
            await TryEnsurePatientProfileAsync(user, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "PATIENT_IDENTITY_CONFLICT")
        {
            return AuthLoginResult.Fail(409, "Số CCCD/Định danh này đã được liên kết với một tài khoản khác trong hệ thống hoặc không khớp thông tin xác thực.", "PATIENT_IDENTITY_CONFLICT");
        }
        _logger?.LogInformation("Google Single Sign-On succeeded: user {UserId} ({Email}) from {Ip}", user.Id, user.Email, client.IpAddress);
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
                    Action = "GOOGLE_SSO_LOGIN",
                    Category = "SECURITY",
                    Severity = "INFO",
                    IpAddress = client.IpAddress ?? "127.0.0.1",
                    TargetEntity = "User",
                    EntityId = user.Id,
                    Details = $"User {user.Email} successfully signed in via Google OAuth2 SSO."
                }, ct);
            }
            catch { /* Skip audit failure */ }
        }

        return AuthLoginResult.Ok(tokens);
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
            1 => new List<string> { "MANAGE_SERVICE_PACKAGE", "MANAGE_USERS", "MANAGE_ROLES", "MANAGE_SCHEDULE" },
            2 => new List<string> { "EXAMINE_PATIENT", "CLINIC_EXAM", "ORDER_TEST", "CREATE_PRESCRIPTION", "VIEW_PATIENT_HISTORY", "VIEW_PATIENT_RECORDS", "VIEW_PATIENTS" },
            3 => new List<string> { "RECEIVE_SAMPLE", "RECEIVE_SAMPLES", "COLLECT_SAMPLE", "COLLECT_SAMPLES", "RUN_ANALYZER", "ENTER_RESULT", "ENTER_LAB_RESULTS", "MANAGE_REAGENTS", "VIEW_ALL_LAB" },
            4 => new List<string> { "CHECK_IN", "CHECKIN_PATIENT", "REGISTER_PATIENTS", "VIEW_PATIENTS", "PROCESS_PAYMENT", "MANAGE_APPOINTMENT", "CREATE_APPOINTMENTS", "MANAGE_QUEUE" },
            5 => new List<string> { "BOOK_APPOINTMENT", "VIEW_OWN_RESULT", "MANAGE_PROFILE" },
            6 => new List<string> { "APPROVE_RESULT", "UNLOCK_QC", "EDIT_PANIC_THRESHOLD", "MANAGE_SCHEDULE", "MANAGE_REAGENTS", "VIEW_PATIENT_RECORDS", "VIEW_ALL_LAB" },
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
        role?.Permissions ?? [],
        u.MedicalProfileCompleted);

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto dto)
    {
        var emailTrimmed = dto.Email.Trim().ToLowerInvariant();
        var usernameTrimmed = !string.IsNullOrWhiteSpace(dto.Username)
            ? dto.Username.Trim()
            : emailTrimmed;

        // Compose detailed address from cascading parts if provided
        var resolvedAddress = dto.Address;
        if (!string.IsNullOrWhiteSpace(dto.StreetAddress))
        {
            var parts = new List<string> { dto.StreetAddress.Trim() };
            if (!string.IsNullOrWhiteSpace(dto.Ward)) parts.Add(dto.Ward.Trim());
            if (!string.IsNullOrWhiteSpace(dto.District)) parts.Add(dto.District.Trim());
            if (!string.IsNullOrWhiteSpace(dto.Province)) parts.Add(dto.Province.Trim());
            resolvedAddress = string.Join(", ", parts);
        }

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
            existingUser.Address = resolvedAddress ?? existingUser.Address;
            if (!string.IsNullOrWhiteSpace(dto.IdentityCard)) existingUser.IdentityCardNumber = dto.IdentityCard.Trim();
            if (dto.DateOfBirth.HasValue) existingUser.DateOfBirth = dto.DateOfBirth.Value;
            if (!string.IsNullOrWhiteSpace(dto.Gender)) existingUser.Gender = dto.Gender;
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
            Address = resolvedAddress,
            IdentityCardNumber = dto.IdentityCard?.Trim(),
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

        // Tự động đồng bộ và tạo hồ sơ Bệnh nhân trong MongoDB patients collection trước khi confirm email (BR-REC-01)
        if (_patientRepository != null && user.RoleId == (int)UserRole.Patient)
        {
            Patient? existingPat = null;
            bool readSuccess = false;
            try
            {
                existingPat = await _patientRepository.GetByAccountIdAsync(user.Id);
                readSuccess = true;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Transient warning: could not verify existing patient profile during OTP verification for user {UserId}", user.Id);
            }

            // Fail-closed: If reading existing patient profile failed, do not assume safe to proceed
            if (!readSuccess)
            {
                return new AuthResponseDto
                {
                    Success = false,
                    Message = "Không thể kiểm tra hồ sơ bệnh nhân do lỗi kết nối cơ sở dữ liệu. Vui lòng bấm xác thực lại.",
                    ErrorCode = "PATIENT_PROFILE_CHECK_FAILED"
                };
            }

            if (existingPat == null)
            {
                var idNum = user.IdentityCardNumber?.Trim();
                var patId = !string.IsNullOrWhiteSpace(idNum) ? $"PAT-{idNum}" : $"PAT-{DateTime.UtcNow.Ticks.ToString()[^10..]}";
                var newPat = new Patient
                {
                    Id = patId,
                    AccountId = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    PhoneNumber = user.PhoneNumber ?? "",
                    IdentityCardNumber = idNum,
                    Address = user.Address ?? "",
                    DateOfBirth = user.DateOfBirth,
                    GenderString = NormalizeGender(user.Gender),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                try
                {
                    await _patientRepository.InsertAsync(newPat);
                }
                catch (PatientDuplicateException ex)
                {
                    _logger?.LogInformation(ex, "Patient duplicate detected during OTP verification for user {UserId}, attempting resolution", user.Id);
                    var (resolved, isConflict) = await ResolvePatientDuplicateAsync(user, idNum, patId);
                    if (!resolved && isConflict)
                    {
                        return new AuthResponseDto
                        {
                            Success = false,
                            Message = "Số CCCD/Định danh này đã được liên kết với một tài khoản khác trong hệ thống. Vui lòng kiểm tra lại hoặc liên hệ hỗ trợ.",
                            ErrorCode = "PATIENT_IDENTITY_CONFLICT"
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to auto-create patient profile in MongoDB for verified OTP user {UserId}", user.Id);
                    // Do NOT confirm email, PRESERVE OTP so user can retry without getting stuck
                    return new AuthResponseDto
                    {
                        Success = false,
                        Message = "Không thể khởi tạo hồ sơ bệnh nhân do lỗi hệ thống. Vui lòng bấm xác thực lại hoặc liên hệ hỗ trợ.",
                        ErrorCode = "PATIENT_PROFILE_CREATION_FAILED"
                    };
                }
            }
        }

        // Confirm email and clear OTP only AFTER patient profile is ensured
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

        try
        {
            await TryEnsurePatientProfileAsync(user);
        }
        catch (InvalidOperationException ex) when (ex.Message == "PATIENT_IDENTITY_CONFLICT")
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "Số CCCD/Định danh này đã được liên kết với một tài khoản khác trong hệ thống hoặc không khớp thông tin xác thực.",
                ErrorCode = "PATIENT_IDENTITY_CONFLICT"
            };
        }

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
            EmailConfirmed = user.EmailConfirmed,
            MedicalProfileCompleted = user.MedicalProfileCompleted
        };
    }

    private static string NormalizeGender(string? g)
    {
        if (string.IsNullOrWhiteSpace(g)) return "Unspecified";
        if (g.Equals("Nam", StringComparison.OrdinalIgnoreCase) || g.Equals("Male", StringComparison.OrdinalIgnoreCase)) return "Male";
        if (g.Equals("Nữ", StringComparison.OrdinalIgnoreCase) || g.Equals("Nu", StringComparison.OrdinalIgnoreCase) || g.Equals("Female", StringComparison.OrdinalIgnoreCase)) return "Female";
        if (g.Equals("Khác", StringComparison.OrdinalIgnoreCase) || g.Equals("Other", StringComparison.OrdinalIgnoreCase)) return "Other";
        return "Unspecified";
    }

    private static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84") && digits.Length > 2)
            digits = "0" + digits[2..];
        return digits;
    }

    private async Task TryEnsurePatientProfileAsync(User user, CancellationToken ct = default)
    {
        if (_patientRepository == null || user.RoleId != (int)UserRole.Patient || string.IsNullOrWhiteSpace(user.Id))
            return;

        try
        {
            var existingPat = await _patientRepository.GetByAccountIdAsync(user.Id, ct);
            if (existingPat != null)
                return;

            var idNum = user.IdentityCardNumber?.Trim();
            var patId = !string.IsNullOrWhiteSpace(idNum) ? $"PAT-{idNum}" : $"PAT-{DateTime.UtcNow.Ticks.ToString()[^10..]}";
            var newPat = new Patient
            {
                Id = patId,
                AccountId = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber ?? "",
                IdentityCardNumber = idNum,
                Address = user.Address ?? "",
                DateOfBirth = user.DateOfBirth,
                GenderString = NormalizeGender(user.Gender),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _patientRepository.InsertAsync(newPat, ct);
            _logger?.LogInformation("Self-healed missing patient profile {PatientId} on login for user {UserId}", patId, user.Id);
        }
        catch (PatientDuplicateException)
        {
            var idNum = user.IdentityCardNumber?.Trim();
            var patId = !string.IsNullOrWhiteSpace(idNum) ? $"PAT-{idNum}" : $"PAT-{DateTime.UtcNow.Ticks.ToString()[^10..]}";
            var (resolved, isConflict) = await ResolvePatientDuplicateAsync(user, idNum, patId, ct);
            if (!resolved && isConflict)
            {
                throw new InvalidOperationException("PATIENT_IDENTITY_CONFLICT");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Non-fatal: could not self-heal patient profile on login for user {UserId}", user.Id);
        }
    }

    /// <summary>
    /// Resolves duplicate collisions during patient creation by verifying account ownership,
    /// linking unassociated walk-in guest profiles, or identifying cross-account identity conflicts.
    /// </summary>
    private async Task<(bool resolved, bool isConflict)> ResolvePatientDuplicateAsync(User user, string? identityNumber, string? patId, CancellationToken ct = default)
    {
        if (_patientRepository == null) return (false, false);

        try
        {
            // 1. Check if user's account already has a patient record
            var byAccount = await _patientRepository.GetByAccountIdAsync(user.Id, ct);
            if (byAccount != null) return (true, false);

            // 2. Check if a patient record exists with this CCCD
            Patient? existing = null;
            if (!string.IsNullOrWhiteSpace(identityNumber))
            {
                try
                {
                    existing = await _patientRepository.FindByIdentityNumberAsync(identityNumber, ct);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Transient warning querying patient by identity number {Cccd}", identityNumber);
                }
            }

            // 3. Check by patient ID fallback
            if (existing == null && !string.IsNullOrWhiteSpace(patId))
            {
                try
                {
                    existing = await _patientRepository.GetByIdOrCodeAsync(patId, ct);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Transient warning querying patient by ID {PatientId}", patId);
                }
            }

            if (existing != null)
            {
                if (existing.AccountId == user.Id)
                    return (true, false);

                // Guest profile created previously without an online account -> link to this user only if multi-factor identity matches
                if (string.IsNullOrWhiteSpace(existing.AccountId))
                {
                    var nameMatches = !string.IsNullOrWhiteSpace(existing.FullName) &&
                                      !string.IsNullOrWhiteSpace(user.FullName) &&
                                      string.Equals(existing.FullName.Trim(), user.FullName.Trim(), StringComparison.OrdinalIgnoreCase);

                    var dobMatches = existing.DateOfBirth.HasValue &&
                                     user.DateOfBirth.HasValue &&
                                     existing.DateOfBirth.Value.Date == user.DateOfBirth.Value.Date;

                    var normExistingPhone = NormalizePhone(existing.PhoneNumber);
                    var normUserPhone = NormalizePhone(user.PhoneNumber);
                    var phoneMatches = !string.IsNullOrEmpty(normExistingPhone) && normExistingPhone == normUserPhone;

                    // Multi-field verification (>= 2 identity attributes: FullName + DateOfBirth or PhoneNumber): prevent unauthorized account takeover
                    bool canLinkGuest = nameMatches && (dobMatches || phoneMatches);

                    if (!canLinkGuest)
                    {
                        _logger?.LogWarning("Patient takeover prevention: Unassociated guest patient {PatientId} (CCCD {Cccd}) cannot be auto-linked to user account {UserId} because identity fields do not match (name: {NameMatches}, dob: {DobMatches}, phone: {PhoneMatches})",
                            existing.Id, identityNumber, user.Id, nameMatches, dobMatches, phoneMatches);
                        return (false, true);
                    }

                    existing.AccountId = user.Id;
                    if (string.IsNullOrWhiteSpace(existing.Email) && !string.IsNullOrWhiteSpace(user.Email))
                    {
                        existing.Email = user.Email;
                    }
                    if (string.IsNullOrWhiteSpace(existing.PhoneNumber) && !string.IsNullOrWhiteSpace(user.PhoneNumber))
                    {
                        existing.PhoneNumber = user.PhoneNumber;
                    }
                    existing.UpdatedAt = DateTime.UtcNow;
                    try
                    {
                        await _patientRepository.UpdateAsync(existing, ct);
                        _logger?.LogInformation("Linked unassociated guest patient profile {PatientId} to user account {UserId} with verified identity match", existing.Id, user.Id);
                        return (true, false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to link guest patient profile {PatientId} to user account {UserId}", existing.Id, user.Id);
                        return (false, false);
                    }
                }

                // Conflict: CCCD already linked to a different account
                _logger?.LogWarning("Patient identity conflict: CCCD {Cccd} belongs to patient {PatientId} linked to account {ExistingAccountId}, not user {UserId}",
                    identityNumber, existing.Id, existing.AccountId, user.Id);
                return (false, true);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Transient error resolving duplicate patient for user {UserId}", user.Id);
        }

        return (false, false);
    }
}
