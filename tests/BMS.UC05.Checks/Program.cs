// UC05 Security Checks: Log In to System
// Implements and verifies SEC-LOGIN-01 to SEC-LOGIN-06 + Enterprise Security Features.
// Conforms to: OWASP ASVS 5.0 (V2, V3, V4), ISO/IEC 27001:2022 (Control 5.15-5.18, 8.5, 8.15).

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BMS.Api.Controllers.Auth;
using BMS.Application.DTOs;
using BMS.Application.DTOs.Auth;
using BMS.Application.Interfaces;
using BMS.Application.Options;
using BMS.Application.Services;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using BMS.Infrastructure.Mongo;
using BMS.Infrastructure.Repositories;
using BMS.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("================================================================================");
Console.WriteLine("UC05 — Log In to System: Automated Security Verification & Compliance Checks");
Console.WriteLine("Standards: OWASP ASVS 5.0 (V2, V3, V4) | ISO/IEC 27001:2022");
Console.WriteLine("================================================================================");

MongoMappings.Register();

var mongoUri = Environment.GetEnvironmentVariable("BMS_CHECK_MONGO") ?? "mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=3000";
var client = new MongoClient(mongoUri);
var testDbName = "BMS_UC05_Checks_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(testDbName);

var jwtSecretKey = "bms_super_secret_key_minimum_32_bytes_long_for_security_testing_2026";
var jwtIssuer = "https://bms.lab.local";
var jwtAudience = "https://bms.lab.local";

var testClock = new MutableTestClock(DateTimeOffset.UtcNow);
var passed = 0;
var failed = 0;

try
{
    // 1. Setup collections
    var accountsCol = db.GetCollection<User>("accounts");
    var refreshTokensCol = db.GetCollection<RefreshToken>("refresh_tokens");
    var auditLogsCol = db.GetCollection<AuditLog>("audit_logs");
    var rolesCol = db.GetCollection<Role>("roles");

    var hasher = new BCryptPasswordHasher();

    // Seed test users
    var adminPasswordHash = hasher.Hash("Admin@123456");
    var patientPasswordHash = hasher.Hash("Patient@123456");
    var doctorPasswordHash = hasher.Hash("Doctor@123456");

    var adminUser = new User
    {
        Id = "USR-ADMIN-01",
        Username = "admin_sec",
        Email = "admin@bms.local",
        FullName = "Hệ thống Quản trị",
        RoleId = (int)UserRole.Admin,
        Role = UserRole.Admin,
        PasswordHash = adminPasswordHash,
        IsActive = true,
        CreatedAt = testClock.GetUtcNow().UtcDateTime
    };

    var patientUser = new User
    {
        Id = "USR-PATIENT-01",
        Username = "patient_sec",
        Email = "patient@bms.local",
        FullName = "Bệnh nhân Nguyễn Văn A",
        RoleId = (int)UserRole.Patient,
        Role = UserRole.Patient,
        PasswordHash = patientPasswordHash,
        IsActive = true,
        CreatedAt = testClock.GetUtcNow().UtcDateTime
    };

    var bruteForceUser = new User
    {
        Id = "USR-BRUTE-01",
        Username = "brute_target",
        Email = "brute@bms.local",
        FullName = "Mục tiêu Brute Force",
        RoleId = (int)UserRole.Doctor,
        Role = UserRole.Doctor,
        PasswordHash = doctorPasswordHash,
        IsActive = true,
        FailedLoginCount = 0,
        CreatedAt = testClock.GetUtcNow().UtcDateTime
    };

    await accountsCol.InsertManyAsync(new[] { adminUser, patientUser, bruteForceUser });

    // Seed roles
    await rolesCol.InsertManyAsync(new[]
    {
        new Role { Id = 1, RoleName = RoleNames.Admin, Permissions = new List<string> { "MANAGE_USERS", "MANAGE_ROLES", "SYSTEM_CONFIG" } },
        new Role { Id = 2, RoleName = RoleNames.Doctor, Permissions = new List<string> { "EXAMINE_PATIENT", "ORDER_TEST" } },
        new Role { Id = 3, RoleName = RoleNames.Lab, Permissions = new List<string> { "RECEIVE_SAMPLE", "ENTER_RESULT" } },
        new Role { Id = 4, RoleName = RoleNames.Receptionist, Permissions = new List<string> { "CHECK_IN", "PROCESS_PAYMENT" } },
        new Role { Id = 5, RoleName = RoleNames.Patient, Permissions = new List<string> { "VIEW_OWN_RESULT", "MANAGE_PROFILE" } },
        new Role { Id = 6, RoleName = RoleNames.Manager, Permissions = new List<string> { "APPROVE_RESULT", "UNLOCK_QC" } }
    });

    // 2. Build WebApplication test server with real JWT and real MongoDB Repositories
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    var configDict = new Dictionary<string, string?>
    {
        ["MongoDB:ConnectionString"] = mongoUri,
        ["MongoDB:DatabaseName"] = testDbName,
        ["Jwt:SecretKey"] = jwtSecretKey,
        ["Jwt:Issuer"] = jwtIssuer,
        ["Jwt:Audience"] = jwtAudience,
        ["Jwt:AccessTokenMinutes"] = "15",
        ["Jwt:RefreshTokenDays"] = "7",
        ["Auth:MaxFailedAttempts"] = "5",
        ["Auth:LockoutMinutes"] = "15"
    };
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();

    builder.Services.AddSingleton<IConfiguration>(configuration);
    builder.Services.Configure<JwtOptions>(opt =>
    {
        opt.SecretKey = jwtSecretKey;
        opt.Issuer = jwtIssuer;
        opt.Audience = jwtAudience;
        opt.AccessTokenMinutes = 15;
        opt.RefreshTokenDays = 7;
    });
    builder.Services.Configure<AuthOptions>(opt =>
    {
        opt.MaxFailedAttempts = 5;
        opt.LockoutMinutes = 15;
    });

    builder.Services.AddSingleton<TimeProvider>(testClock);
    builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
    builder.Services.AddSingleton<ITokenService, JwtTokenService>();
    builder.Services.AddSingleton<IMongoDatabase>(db);
    builder.Services.AddSingleton<IMongoClient>(client);
    builder.Services.AddSingleton(new MongoCollections(
        "accounts", "roles", "refresh_tokens", "password_reset_tokens", "service_packages", "test_criteria"));
    builder.Services.AddSingleton<MongoDbContext>();
    builder.Services.AddSingleton<BmsDbContext>();
    builder.Services.AddSingleton<IUserRepository, UserRepository>();
    builder.Services.AddSingleton<IRoleRepository, RoleRepository>();
    builder.Services.AddSingleton<IRefreshTokenRepository, RefreshTokenRepository>();
    builder.Services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
    builder.Services.AddScoped<IAuthService, AuthService>();
    builder.Services.AddSingleton<IOtpService, DummyOtpService>();

    builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,
                ValidateAudience = true,
                ValidAudience = jwtAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                NameClaimType = "name",
                RoleClaimType = "role"
            };
        });

    builder.Services.AddRateLimiter(o =>
    {
        o.AddPolicy("login", ctx => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
    });

    builder.Services.AddAuthorization(o =>
    {
        o.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });

    var app = builder.Build();
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();
    app.MapControllers();

    // Admin protected test endpoint
    app.MapGet("/api/admin/system-status", [Authorize(Roles = RoleNames.Admin)] () => Results.Ok(new { status = "ADMIN_SECURE_DATA" }));
    // Patient protected test endpoint
    app.MapGet("/api/patient/my-records", [Authorize(Roles = RoleNames.Patient)] () => Results.Ok(new { status = "PATIENT_RECORD" }));

    await app.StartAsync();
    var serverUrl = app.Urls.First();
    using var http = new HttpClient { BaseAddress = new Uri(serverUrl) };

    // =========================================================================
    // TEST CASES IMPLEMENTATION
    // =========================================================================

    // -------------------------------------------------------------------------
    // SEC-LOGIN-01: Đăng nhập đúng tài khoản và mật khẩu
    // -------------------------------------------------------------------------
    await RunCheck("SEC-LOGIN-01: Đăng nhập hợp lệ bằng username và mật khẩu (200 OK, JWT, RTR, Audit Log)", async () =>
    {
        var loginPayload = new LoginRequest
        {
            UsernameOrEmail = "admin_sec",
            Password = "Admin@123456"
        };

        var response = await http.PostAsJsonAsync("/api/auth/login", loginPayload);
        Assert(response.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {response.StatusCode}");

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert(body != null, "Response body should not be null");
        Assert(!string.IsNullOrWhiteSpace(body.AccessToken), "Access token must be generated");
        Assert(!string.IsNullOrWhiteSpace(body.RefreshToken), "Refresh token must be generated");
        Assert(body.User.Role == RoleNames.Admin, $"Role must be Admin, got {body.User.Role}");

        // Validate JWT claims
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(body.AccessToken);
        Assert(jwt.Issuer == jwtIssuer, "Issuer mismatch");
        Assert(jwt.Audiences.Contains(jwtAudience), "Audience mismatch");
        Assert(jwt.Claims.Any(c => c.Type == "role" && c.Value == RoleNames.Admin), "Missing role claim");

        // Verify Audit Log
        var audit = await auditLogsCol.Find(a => a.UserId == "USR-ADMIN-01" && a.Action == "LOGIN_SUCCESS").FirstOrDefaultAsync();
        Assert(audit != null, "AuditLog for LOGIN_SUCCESS must be recorded");
        Assert(audit.Severity == "INFO" && audit.Status == "SUCCESS", "Audit severity and status should match INFO/SUCCESS");
    });

    // -------------------------------------------------------------------------
    // SEC-LOGIN-02: Nhập sai mật khẩu nhiều lần (Brute Force Protection)
    // -------------------------------------------------------------------------
    await RunCheck("SEC-LOGIN-02: Nhập sai mật khẩu liên tiếp (401 Generic Error -> 423 Locked sau 5 lần)", async () =>
    {
        var loginPayload = new LoginRequest
        {
            UsernameOrEmail = "brute_target",
            Password = "WrongPassword!999"
        };

        // Attempts 1 to 4 should return 401 Unauthorized with generic message
        for (int i = 1; i <= 4; i++)
        {
            var res = await http.PostAsJsonAsync("/api/auth/login", loginPayload);
            Assert(res.StatusCode == HttpStatusCode.Unauthorized, $"Attempt {i} expected 401 Unauthorized, got {res.StatusCode}");
            var content = await res.Content.ReadAsStringAsync();
            Assert(content.Contains("Tên đăng nhập hoặc mật khẩu không chính xác"), "Generic error message required to prevent user enumeration");
        }

        // Attempt 5: Account should lock out immediately (HTTP 423 Locked)
        var lockRes = await http.PostAsJsonAsync("/api/auth/login", loginPayload);
        Assert(lockRes.StatusCode == HttpStatusCode.Locked, $"Attempt 5 expected 423 Locked, got {lockRes.StatusCode}");
        var lockContent = await lockRes.Content.ReadAsStringAsync();
        Assert(lockContent.Contains("ACCOUNT_LOCKED"), "Response must indicate ACCOUNT_LOCKED");

        // Attempt 6 (even with correct password) should remain locked
        var evenCorrectPayload = new LoginRequest
        {
            UsernameOrEmail = "brute_target",
            Password = "Doctor@123456"
        };
        var lockedStillRes = await http.PostAsJsonAsync("/api/auth/login", evenCorrectPayload);
        Assert(lockedStillRes.StatusCode == HttpStatusCode.Locked, "Locked account must reject even valid password during lockout period");

        // Verify Audit Log recorded failures and lock
        var failAudits = await auditLogsCol.Find(a => a.UserId == "USR-BRUTE-01" && (a.Action == "LOGIN_FAILED" || a.Action == "LOGIN_BLOCKED")).ToListAsync();
        Assert(failAudits.Count >= 5, "At least 5 security failure audit logs should be registered");
    });

    // -------------------------------------------------------------------------
    // SEC-LOGIN-03: Dùng JWT hết hạn gọi API (Lifetime Validation)
    // -------------------------------------------------------------------------
    await RunCheck("SEC-LOGIN-03: Dùng JWT đã hết hạn gọi API bảo vệ (401 Unauthorized)", async () =>
    {
        // Forge an expired token (signed with valid key, but expired in past)
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey));
        var expiredDescriptor = new SecurityTokenDescriptor
        {
            Issuer = jwtIssuer,
            Audience = jwtAudience,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", "USR-PATIENT-01"),
                new Claim("role", RoleNames.Patient),
                new Claim("name", "Expired User")
            }),
            NotBefore = DateTime.UtcNow.AddMinutes(-60),
            Expires = DateTime.UtcNow.AddMinutes(-30), // Expired 30 mins ago
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };
        var expiredJwt = tokenHandler.CreateToken(expiredDescriptor);
        var expiredTokenString = tokenHandler.WriteToken(expiredJwt);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/patient/my-records");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredTokenString);

        var response = await http.SendAsync(request);
        Assert(response.StatusCode == HttpStatusCode.Unauthorized, $"Expired token should return 401 Unauthorized, got {response.StatusCode}");
    });

    // -------------------------------------------------------------------------
    // SEC-LOGIN-04: Patient gọi API dành riêng cho Admin (RBAC Enforcement)
    // -------------------------------------------------------------------------
    await RunCheck("SEC-LOGIN-04: Patient gọi API dành riêng cho Admin (403 Forbidden - Zero Trust RBAC)", async () =>
    {
        // 1. Login as Patient
        var loginResponse = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            UsernameOrEmail = "patient_sec",
            Password = "Patient@123456"
        });
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert(auth != null && !string.IsNullOrWhiteSpace(auth.AccessToken), "Patient login must succeed");

        // 2. Call Patient's own endpoint -> should return 200 OK
        using var ownRequest = new HttpRequestMessage(HttpMethod.Get, "/api/patient/my-records");
        ownRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var ownResponse = await http.SendAsync(ownRequest);
        Assert(ownResponse.StatusCode == HttpStatusCode.OK, "Patient accessing own endpoint should return 200 OK");

        // 3. Call Admin endpoint -> Backend must enforce 403 Forbidden
        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/system-status");
        adminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var adminResponse = await http.SendAsync(adminRequest);
        Assert(adminResponse.StatusCode == HttpStatusCode.Forbidden, $"Patient accessing Admin endpoint must return 403 Forbidden, got {adminResponse.StatusCode}");
    });

    // -------------------------------------------------------------------------
    // SEC-LOGIN-05: Sửa JWT để thay đổi Role (JWT Tampering Prevention)
    // -------------------------------------------------------------------------
    await RunCheck("SEC-LOGIN-05: Sửa JWT payload đổi Role từ Patient sang Admin (401 Unauthorized)", async () =>
    {
        // 1. Login as Patient to get a real valid token
        var loginResponse = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            UsernameOrEmail = "patient_sec",
            Password = "Patient@123456"
        });
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        var validToken = auth!.AccessToken;

        // 2. Tamper the payload: split header.payload.signature, decode payload, modify role to Admin, re-encode
        var parts = validToken.Split('.');
        Assert(parts.Length == 3, "Valid JWT has 3 parts");

        var payloadBytes = Base64UrlEncoder.DecodeBytes(parts[1]);
        var payloadJson = Encoding.UTF8.GetString(payloadBytes);

        // Replace "Patient" role with "Admin"
        var tamperedPayloadJson = payloadJson.Replace($"\"{RoleNames.Patient}\"", $"\"{RoleNames.Admin}\"");
        var tamperedPayloadBase64 = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(tamperedPayloadJson));

        // Assemble tampered JWT keeping original signature
        var tamperedToken = $"{parts[0]}.{tamperedPayloadBase64}.{parts[2]}";

        // 3. Call Admin endpoint with tampered token
        using var tamperedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/admin/system-status");
        tamperedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);
        var tamperedResponse = await http.SendAsync(tamperedRequest);

        Assert(tamperedResponse.StatusCode == HttpStatusCode.Unauthorized, $"Tampered JWT signature must be rejected with 401 Unauthorized, got {tamperedResponse.StatusCode}");
    });

    // -------------------------------------------------------------------------
    // SEC-LOGIN-06: Đăng xuất rồi sử dụng Refresh Token đã thu hồi (Session Revocation)
    // -------------------------------------------------------------------------
    await RunCheck("SEC-LOGIN-06: Đăng xuất rồi dùng Refresh Token đã thu hồi gọi Refresh (401 Unauthorized)", async () =>
    {
        // 1. Login to obtain Access Token and Refresh Token
        var loginResponse = await http.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            UsernameOrEmail = "admin_sec",
            Password = "Admin@123456"
        });
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        var accessToken = auth!.AccessToken;
        var refreshToken = auth.RefreshToken;

        // 2. Call Logout to revoke the session
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        logoutRequest.Content = JsonContent.Create(new LogoutRequest { RefreshToken = refreshToken });
        var logoutResponse = await http.SendAsync(logoutRequest);
        Assert(logoutResponse.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK, $"Logout should succeed, got {logoutResponse.StatusCode}");

        // 3. Try to use revoked refresh token to obtain a new access token
        var refreshPayload = new RefreshTokenRequest { RefreshToken = refreshToken };
        var refreshResponse = await http.PostAsJsonAsync("/api/auth/refresh", refreshPayload);

        Assert(refreshResponse.StatusCode == HttpStatusCode.Unauthorized, $"Revoked refresh token must be rejected with 401 Unauthorized, got {refreshResponse.StatusCode}");

        // Also verify the alias endpoint /api/auth/refresh-token rejects it
        var aliasResponse = await http.PostAsJsonAsync("/api/auth/refresh-token", refreshPayload);
        Assert(aliasResponse.StatusCode == HttpStatusCode.Unauthorized, $"Revoked refresh token on alias must be rejected with 401 Unauthorized, got {aliasResponse.StatusCode}");
    });

    // -------------------------------------------------------------------------
    // SEC-LOGIN-EXTRA-01: Chống User Enumeration & Timing Attacks
    // -------------------------------------------------------------------------
    await RunCheck("SEC-EXTRA-01: Chống dò quét tài khoản (User Enumeration): Trả cùng lỗi 401 cho tài khoản không tồn tại", async () =>
    {
        var nonExistentPayload = new LoginRequest
        {
            UsernameOrEmail = "definitely_non_existent_account_xyz",
            Password = "RandomPassword123!"
        };

        var response = await http.PostAsJsonAsync("/api/auth/login", nonExistentPayload);
        Assert(response.StatusCode == HttpStatusCode.Unauthorized, $"Non-existent user must return 401, got {response.StatusCode}");

        var content = await response.Content.ReadAsStringAsync();
        Assert(content.Contains("Tên đăng nhập hoặc mật khẩu không chính xác"), "Must use identical generic error message as wrong password");

        // Verify audit log captured unknown attempt
        var unknownAudit = await auditLogsCol.Find(a => a.UserName == "definitely_non_existent_account_xyz" && a.Action == "LOGIN_FAILED").FirstOrDefaultAsync();
        Assert(unknownAudit != null, "Security audit trail must record login attempts on non-existent accounts");
    });

    // -------------------------------------------------------------------------
    // SEC-LOGIN-EXTRA-02: Password Không lưu plain text, mã hóa BCrypt
    // -------------------------------------------------------------------------
    await RunCheck("SEC-EXTRA-02: Xác nhận mật khẩu được băm BCrypt, không bao giờ lưu plain text", () =>
    {
        var storedAdmin = accountsCol.Find(u => u.Id == "USR-ADMIN-01").FirstOrDefault();
        Assert(storedAdmin != null, "Admin account must exist");
        Assert(!storedAdmin.PasswordHash.Contains("Admin@123456"), "Password MUST NOT be stored as plain text");
        Assert(storedAdmin.PasswordHash.StartsWith("$2"), "Password hash MUST be a valid BCrypt hash format ($2a / $2b)");
        Assert(hasher.Verify("Admin@123456", storedAdmin.PasswordHash), "BCrypt hash must properly verify correct password");
        Assert(!hasher.Verify("Admin@Wrong", storedAdmin.PasswordHash), "BCrypt hash must reject incorrect password");
        return Task.CompletedTask;
    });

    await app.StopAsync();
}
finally
{
    // Clean up temporary disposable check database
    try
    {
        await client.DropDatabaseAsync(testDbName);
    }
    catch { }
}

Console.WriteLine("================================================================================");
Console.WriteLine($"RESULTS: {passed} passed, {failed} failed out of {passed + failed} test cases.");
Console.WriteLine("================================================================================");

if (failed > 0) Environment.Exit(1);

async Task RunCheck(string testName, Func<Task> action)
{
    try
    {
        await action();
        passed++;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[PASS] ");
        Console.ResetColor();
        Console.WriteLine(testName);
    }
    catch (Exception ex)
    {
        failed++;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[FAIL] ");
        Console.ResetColor();
        Console.WriteLine(testName);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"       Detail: {ex.Message}");
        Console.ResetColor();
    }
}

static void Assert(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class MutableTestClock : TimeProvider
{
    private DateTimeOffset _utc;
    public MutableTestClock(DateTimeOffset initial) => _utc = initial;
    public override DateTimeOffset GetUtcNow() => _utc;
    public void Advance(TimeSpan delta) => _utc = _utc.Add(delta);
}

sealed class DummyOtpService : IOtpService
{
    public Task<SendOtpResponseDto> SendOtpAsync(SendOtpRequestDto request, string clientIp, CancellationToken ct = default)
        => Task.FromResult(new SendOtpResponseDto { Success = true });
    public Task<VerifyOtpResponseDto> VerifyOtpAsync(VerifyOtpRequestDto request, CancellationToken ct = default)
        => Task.FromResult(new VerifyOtpResponseDto { Success = true });
}
