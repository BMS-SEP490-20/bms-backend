using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BMS.Api.Controllers.PatientDomain;
using BMS.Application.DTOs.Patients;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Mongo;
using BMS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

Console.OutputEncoding = Encoding.UTF8;
MongoMappings.Register();

Console.WriteLine("================================================================================");
Console.WriteLine("UC25 — Create Guest Profile: Automated Security & Compliance Checks");
Console.WriteLine("Standards: ISO 15189:2022 | ISO 20658:2023 | OWASP ASVS (Input & Access Control)");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc25_test_" + Guid.NewGuid().ToString("N")[..8];
var client = new MongoClient(mongoUri);
var db = client.GetDatabase(testDbName);

const string jwtSecretKey = "bms_secret_key_for_testing_purposes_only_minimum_32_bytes_long!!";
const string jwtIssuer = "bms-test-issuer";
const string jwtAudience = "bms-test-audience";
var testClock = new MutableTestClock(DateTimeOffset.UtcNow);

// 1. Set up WebApplication Host
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
    ["Jwt:Audience"] = jwtAudience
};
var configuration = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();
builder.Services.AddSingleton<IConfiguration>(configuration);
builder.Services.AddSingleton<BMS.Infrastructure.Data.MongoDbContext>();

builder.Services.AddSingleton<TimeProvider>(testClock);
builder.Services.AddSingleton<IMongoDatabase>(db);
builder.Services.AddSingleton<IMongoClient>(client);
builder.Services.AddSingleton(new MongoCollections(
    Users: "accounts",
    Roles: "roles",
    RefreshTokens: "refresh_tokens",
    ServicePackages: "service_packages",
    PasswordResetTokens: "password_reset_tokens",
    TestCriteria: "test_criteria"));

builder.Services.AddSingleton<IPatientRepository, PatientRepository>();
builder.Services.AddSingleton<IUserRepository, UserRepository>();
builder.Services.AddSingleton<ISequenceGenerator, MongoSequenceGenerator>();
builder.Services.AddSingleton<BMS.Core.Interfaces.IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IWalkInService, WalkInService>();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(PatientsController).Assembly);

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
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.StartAsync();
var serverAddress = app.Urls.First();
using var http = new HttpClient { BaseAddress = new Uri(serverAddress) };

// Helper to issue tokens
string GenerateToken(string userId, string role, string name = "User")
{
    var claims = new[]
    {
        new Claim("sub", userId),
        new Claim(ClaimTypes.NameIdentifier, userId),
        new Claim(ClaimTypes.Role, role),
        new Claim(ClaimTypes.Name, name)
    };
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(30),
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

var receptionistToken = GenerateToken("USR-RECEPTION-01", RoleNames.Receptionist, "Tran Thi Le Tan");
var patientToken = GenerateToken("USR-PATIENT-01", RoleNames.Patient, "Nguyen Van Benh Nhan");
var doctorToken = GenerateToken("USR-DOCTOR-01", RoleNames.Doctor, "Bac Si Nguyen");

void SetAuth(HttpClient client, string? token)
{
    if (token != null)
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    else
        client.DefaultRequestHeaders.Authorization = null;
}

try
{
    // =========================================================================
    // UC25: CREATE GUEST PROFILE
    // =========================================================================

    string createdPatientId1 = "";

    await RunCheck("SEC-GUEST-01: Tạo hồ sơ khách vãng lai đủ thông tin hợp lệ -> 201 Created & sinh PatientId duy nhất", async () =>
    {
        SetAuth(http, receptionistToken);
        var req = new CreateGuestProfileRequest
        {
            FullName = "Nguyen Van A",
            DateOfBirth = new DateTime(1995, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Male",
            PhoneNumber = "0901234567",
            Email = "nguyenvana@gmail.com",
            Address = "123 Le Loi, Q1, TP.HCM",
            IdentityNumber = "079195000001"
        };

        var res = await http.PostAsJsonAsync("/api/patients/guests", req);
        Assert(res.StatusCode == HttpStatusCode.Created, $"Expected 201 Created, got {res.StatusCode}");
        var dto = await res.Content.ReadFromJsonAsync<PatientInformationDto>();
        Assert(dto != null, "Response should contain PatientInformationDto");
        Assert(!string.IsNullOrWhiteSpace(dto!.Id) && dto.Id.StartsWith("PAT-"), $"PatientId must be formatted like PAT-xxx, got {dto.Id}");
        Assert(dto.FullName == "Nguyen Van A", "FullName must match");
        Assert(dto.IsGuest, "IsGuest must be true for guest profile");
        Assert(dto.ProfileType == "Guest", $"ProfileType must be 'Guest', got {dto.ProfileType}");
        Assert(dto.CreatedBy == "USR-RECEPTION-01", $"CreatedBy must be 'USR-RECEPTION-01', got {dto.CreatedBy}");

        createdPatientId1 = dto.Id;
    });

    await RunCheck("SEC-GUEST-02: Chuẩn ISO 15189: Không dùng tên làm khóa duy nhất (Cùng tên 'Nguyen Van A' nhưng khác SĐT/ngày sinh -> Sinh PatientId thứ hai riêng biệt)", async () =>
    {
        SetAuth(http, receptionistToken);
        var req = new CreateGuestProfileRequest
        {
            FullName = "Nguyen Van A", // Cùng tên nhưng người khác
            DateOfBirth = new DateTime(1988, 12, 10, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Male",
            PhoneNumber = "0987654321", // SĐT khác
            IdentityNumber = "079188000002"
        };

        var res = await http.PostAsJsonAsync("/api/patients/guests", req);
        Assert(res.StatusCode == HttpStatusCode.Created, $"Expected 201 Created, got {res.StatusCode}");
        var dto = await res.Content.ReadFromJsonAsync<PatientInformationDto>();
        Assert(dto != null && dto.Id != createdPatientId1, "Second patient must get a unique different PatientId");
        Assert(dto.FullName == "Nguyen Van A", "FullName should be identical but uniquely identified");
    });

    await RunCheck("SEC-WALKIN-01: Kiểm tra endpoint POST /api/walk-in/register -> Phải thành công 201 Created", async () =>
    {
        SetAuth(http, receptionistToken);
        var req = new BMS.WalkIn.DTOs.RegisterWalkInRequest
        {
            FullName = "Nguyen Van Walkin",
            DateOfBirth = new DateTime(1990, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Male",
            Phone = "0912345678",
            IdentityNumber = "079190000003",
            Address = "789 Tran Hung Dao, Q5",
            Priority = "NORMAL"
        };

        var res = await http.PostAsJsonAsync("/api/walk-in/register", req);
        var bodyStr = await res.Content.ReadAsStringAsync();
        Assert(res.StatusCode == HttpStatusCode.Created, $"Expected 201 Created from /api/walk-in/register, got {res.StatusCode}. Body: {bodyStr}");
    });

    await RunCheck("SEC-WALKIN-02: Kiểm tra POST /api/walk-in/register khi trùng CCCD -> Phải trả về 409 Conflict chứ không phải 500", async () =>
    {
        SetAuth(http, receptionistToken);
        var req = new BMS.WalkIn.DTOs.RegisterWalkInRequest
        {
            FullName = "Khach Trung CCCD",
            DateOfBirth = new DateTime(1992, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Female",
            Phone = "0988776655",
            IdentityNumber = "079190000003", // Trùng với SEC-WALKIN-01
            Address = "123 CMT8",
            Priority = "NORMAL"
        };

        var res = await http.PostAsJsonAsync("/api/walk-in/register", req);
        var bodyStr = await res.Content.ReadAsStringAsync();
        Assert(res.StatusCode == HttpStatusCode.Conflict, $"Expected 409 Conflict for duplicate CCCD on /api/walk-in/register, got {res.StatusCode}. Body: {bodyStr}");
    });

    await RunCheck("SEC-WALKIN-03: Kiểm tra POST /api/walk-in/register khi ngày sinh ở tương lai (2030) -> Phải trả về 400 Bad Request", async () =>
    {
        SetAuth(http, receptionistToken);
        var req = new BMS.WalkIn.DTOs.RegisterWalkInRequest
        {
            FullName = "VU HOANG ANH",
            DateOfBirth = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Male",
            Phone = "0977112233",
            IdentityNumber = "079200001111"
        };

        var res = await http.PostAsJsonAsync("/api/walk-in/register", req);
        var bodyStr = await res.Content.ReadAsStringAsync();
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request for future DOB on /api/walk-in/register, got {res.StatusCode}. Body: {bodyStr}");
        Assert(bodyStr.Contains("tương lai") || bodyStr.Contains("thời điểm hiện tại"), "Should mention future date of birth error message");
    });

    await RunCheck("SEC-GUEST-03: Thiếu họ tên (FullName rỗng) -> Bị từ chối (400 Bad Request)", async () =>
    {
        SetAuth(http, receptionistToken);
        var req = new CreateGuestProfileRequest
        {
            FullName = "", // Rỗng!
            DateOfBirth = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Female",
            PhoneNumber = "0912345678"
        };

        var res = await http.PostAsJsonAsync("/api/patients/guests", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request, got {res.StatusCode}");
    });

    await RunCheck("SEC-GUEST-04: Ngày sinh ở tương lai -> Bị từ chối (400 Bad Request)", async () =>
    {
        SetAuth(http, receptionistToken);
        var req = new CreateGuestProfileRequest
        {
            FullName = "Tran Van Tuong Lai",
            DateOfBirth = DateTime.UtcNow.AddDays(10), // Tương lai!
            Gender = "Male",
            PhoneNumber = "0933333333"
        };

        var res = await http.PostAsJsonAsync("/api/patients/guests", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request for future DOB, got {res.StatusCode}");
    });

    await RunCheck("SEC-GUEST-05: Số điện thoại không đúng định dạng Việt Nam -> Bị từ chối (400 Bad Request)", async () =>
    {
        SetAuth(http, receptionistToken);
        var req = new CreateGuestProfileRequest
        {
            FullName = "Le Thi Sai Sdt",
            DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Female",
            PhoneNumber = "123456" // Sai định dạng số điện thoại VN!
        };

        var res = await http.PostAsJsonAsync("/api/patients/guests", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request for invalid phone, got {res.StatusCode}");
    });

    await RunCheck("SEC-GUEST-06: Trùng hồ sơ (trùng Họ tên + Số điện thoại + Ngày sinh) -> Bị từ chối (409 Conflict / PATIENT_DUPLICATE_PROFILE)", async () =>
    {
        SetAuth(http, receptionistToken);
        // Duplicate profile with patient 1
        var req = new CreateGuestProfileRequest
        {
            FullName = "Nguyen Van A",
            DateOfBirth = new DateTime(1995, 5, 20, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Male",
            PhoneNumber = "0901234567" // Trùng bộ 3 Họ tên, Ngày sinh, SĐT
        };

        var res = await http.PostAsJsonAsync("/api/patients/guests", req);
        Assert(res.StatusCode == HttpStatusCode.Conflict, $"Expected 409 Conflict, got {res.StatusCode}");
        var content = await res.Content.ReadAsStringAsync();
        Assert(content.Contains("PATIENT_DUPLICATE_PROFILE") || content.Contains("đã tồn tại"), "Should indicate duplicate profile collision");
    });

    await RunCheck("SEC-GUEST-07: Trùng số CCCD 12 chữ số đã tồn tại -> Bị từ chối (409 Conflict / MSG-ERR-REC-01)", async () =>
    {
        SetAuth(http, receptionistToken);
        var req = new CreateGuestProfileRequest
        {
            FullName = "Hoang Van Trung Cccd",
            DateOfBirth = new DateTime(1992, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Male",
            PhoneNumber = "0944444444",
            IdentityNumber = "079195000001" // Đã dùng ở bệnh nhân 1!
        };

        var res = await http.PostAsJsonAsync("/api/patients/guests", req);
        Assert(res.StatusCode == HttpStatusCode.Conflict, $"Expected 409 Conflict, got {res.StatusCode}");
        var content = await res.Content.ReadAsStringAsync();
        Assert(content.Contains("MSG-ERR-REC-01") || content.Contains("CCCD"), "Should indicate duplicate CCCD collision");
    });

    await RunCheck("SEC-GUEST-08: Người không có quyền (Patient / Chưa đăng nhập) gọi API tạo hồ sơ khách -> 401 / 403 Forbidden", async () =>
    {
        var req = new CreateGuestProfileRequest
        {
            FullName = "Khach Vang Lai",
            DateOfBirth = new DateTime(1999, 9, 9, 0, 0, 0, DateTimeKind.Utc),
            Gender = "Male",
            PhoneNumber = "0955555555"
        };

        // 1. Unauthenticated -> 401
        SetAuth(http, null);
        var unauthRes = await http.PostAsJsonAsync("/api/patients/guests", req);
        Assert(unauthRes.StatusCode == HttpStatusCode.Unauthorized, $"Expected 401 Unauthorized, got {unauthRes.StatusCode}");

        // 2. Patient -> 403 Forbidden
        SetAuth(http, patientToken);
        var patientRes = await http.PostAsJsonAsync("/api/patients/guests", req);
        Assert(patientRes.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden, got {patientRes.StatusCode}");
    });

    await RunCheck("SEC-GUEST-09: Client giả mạo createdBy trong JSON body -> Backend bỏ qua và ghi nhận đúng userId từ JWT Context", async () =>
    {
        SetAuth(http, receptionistToken); // Authenticated as USR-RECEPTION-01
        var spoofedJson = """
        {
            "fullName": "Bui Thi Chinh Hang",
            "dateOfBirth": "2001-07-15T00:00:00Z",
            "gender": "Female",
            "phoneNumber": "0966666666",
            "createdBy": "USR-ADMIN-SUPER-HACKED",
            "isGuest": false
        }
        """;
        var content = new StringContent(spoofedJson, Encoding.UTF8, "application/json");
        var res = await http.PostAsync("/api/patients/guests", content);
        Assert(res.StatusCode == HttpStatusCode.Created, "Creation should succeed");

        var dto = await res.Content.ReadFromJsonAsync<PatientInformationDto>();
        Assert(dto != null, "Response should have DTO");
        Assert(dto!.CreatedBy == "USR-RECEPTION-01", $"CreatedBy must be 'USR-RECEPTION-01', got '{dto.CreatedBy}'");
        Assert(dto.IsGuest == true, "IsGuest must remain true regardless of client spoofing");

        // Verify direct from Mongo database
        var patientCol = db.GetCollection<Patient>("patients");
        var savedPatient = await patientCol.Find(p => p.Id == dto.Id).FirstOrDefaultAsync();
        Assert(savedPatient != null && savedPatient.CreatedBy == "USR-RECEPTION-01", "Database must store authenticated userId");
    });

    await RunCheck("SEC-GUEST-10: Ghi nhận sự kiện GUEST_PROFILE_CREATED vào Audit Trail (ISO 15189 / ISO 27789)", async () =>
    {
        var auditCol = db.GetCollection<AuditLog>("audit_logs");
        var log = await auditCol.Find(a => a.Action == "GUEST_PROFILE_CREATED" && a.UserId == "USR-RECEPTION-01")
            .SortByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();

        Assert(log != null, "Audit log must be created for GUEST_PROFILE_CREATED");
        Assert(log!.Category == "PATIENT_IDENTITY", "Audit category must be PATIENT_IDENTITY");
        Assert(log.Details.Contains("***"), "Audit trail must contain masked PII (ISO 27789 / HIPAA compliance)");
    });
}
finally
{
    await client.DropDatabaseAsync(testDbName);
    await app.StopAsync();
    await app.DisposeAsync();
}

Console.WriteLine("================================================================================");
Console.WriteLine($"RESULTS: {passed} passed, {failed} failed out of {passed + failed} test cases.");
Console.WriteLine("================================================================================");

if (failed > 0) Environment.Exit(1);

async Task RunCheck(string testName, Func<Task> testFunc)
{
    try
    {
        await testFunc();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[PASS] ");
        Console.ResetColor();
        Console.WriteLine(testName);
        passed++;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[FAIL] ");
        Console.ResetColor();
        Console.WriteLine(testName);
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"       Detail: {ex.Message}");
        Console.ResetColor();
        failed++;
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
