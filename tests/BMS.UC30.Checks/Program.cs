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
Console.WriteLine("UC30 — View Patient Information: Automated Security & Anti-IDOR Compliance Checks");
Console.WriteLine("Standards: ISO 15189:2022 | ISO 27789:2021 (EHR Audit) | OWASP ASVS (Anti-IDOR)");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc30_test_" + Guid.NewGuid().ToString("N")[..8];
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
builder.Services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IPatientService, PatientService>();

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
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, BMS.Api.Authorization.PermissionPolicyProvider>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, BMS.Api.Authorization.PermissionHandler>();

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

var patientAToken = GenerateToken("USR-PATIENT-01", RoleNames.Patient, "Nguyen Van A");
var patientBToken = GenerateToken("USR-PATIENT-02", RoleNames.Patient, "Tran Thi B");
var doctorToken = GenerateToken("USR-DOCTOR-01", RoleNames.Doctor, "Bac Si Le Van C");
var receptionistToken = GenerateToken("USR-RECEPTION-01", RoleNames.Receptionist, "Le Tan Pham Thi D");
var labToken = GenerateToken("USR-LAB-01", RoleNames.Lab, "Ky Thuat Vien Lab E");
var adminToken = GenerateToken("USR-ADMIN-01", RoleNames.Admin, "Admin System");

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
    // SEED DATA FOR PATIENT A AND PATIENT B
    // =========================================================================
    var patientsCollection = db.GetCollection<Patient>("patients");
    var auditLogsCollection = db.GetCollection<AuditLog>("audit_logs");

    var patientA = new Patient
    {
        Id = "PAT-TEST-00001",
        AccountId = "USR-PATIENT-01",
        PatientCodeField = "BN-2026-00001",
        FullName = "Nguyen Van A",
        DateOfBirth = new DateTime(1995, 5, 20, 0, 0, 0, DateTimeKind.Utc),
        GenderString = "Male",
        PhoneNumber = "0901111111",
        IdentityCardNumber = "079195000001",
        Address = "123 Le Loi, Q1, TP.HCM",
        BloodType = "O+",
        IsGuest = false,
        CreatedBy = "USR-RECEPTION-01",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    var patientB = new Patient
    {
        Id = "PAT-TEST-00002",
        AccountId = "USR-PATIENT-02",
        PatientCodeField = "BN-2026-00002",
        FullName = "Tran Thi B",
        DateOfBirth = new DateTime(1998, 8, 15, 0, 0, 0, DateTimeKind.Utc),
        GenderString = "Female",
        PhoneNumber = "0902222222",
        IdentityCardNumber = "079195000002",
        Address = "456 Nguyen Hue, Q1, TP.HCM",
        BloodType = "A+",
        IsGuest = false,
        CreatedBy = "USR-RECEPTION-01",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    await patientsCollection.InsertManyAsync([patientA, patientB]);

    // =========================================================================
    // TEST CASES FOR UC30
    // =========================================================================

    await RunCheck("SEC-PAT-VIEW-01: Patient A xem hồ sơ của chính mình (qua patientId) -> 200 OK với đúng thông tin", async () =>
    {
        SetAuth(http, patientAToken);
        var res = await http.GetAsync($"/api/patients/{patientA.Id}");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");

        var dto = await res.Content.ReadFromJsonAsync<PatientInformationDto>();
        Assert(dto != null, "Response should contain PatientInformationDto");
        Assert(dto!.Id == patientA.Id, $"PatientId must match {patientA.Id}, got {dto.Id}");
        Assert(dto.FullName == "Nguyen Van A", "FullName must match Patient A");
        Assert(dto.PatientCode == "BN-2026-00001", "PatientCode must match BN-2026-00001");
    });

    await RunCheck("SEC-PAT-VIEW-02 [CRITICAL IDOR]: Patient A cố tình sửa patientId sang Patient B -> 403 Forbidden", async () =>
    {
        SetAuth(http, patientAToken);
        // Patient A requests Patient B's ID directly
        var res = await http.GetAsync($"/api/patients/{patientB.Id}");
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden for IDOR attack, got {res.StatusCode}");
    });

    await RunCheck("SEC-PAT-VIEW-03 [IDOR by PatientCode]: Patient A tra cứu mã bệnh nhân của Patient B -> 403 Forbidden", async () =>
    {
        SetAuth(http, patientAToken);
        // Patient A requests Patient B's PatientCode
        var res = await http.GetAsync($"/api/patients/{patientB.PatientCode}");
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden when accessing Patient B by code, got {res.StatusCode}");
    });

    await RunCheck("SEC-PAT-VIEW-04 [Audit Trail - ISO 27789]: Ghi nhận nỗ lực IDOR vào Audit Log với Status=ACCESS_DENIED, Severity=WARNING", async () =>
    {
        var logs = await auditLogsCollection.Find(a => a.Action == "VIEW_PATIENT_INFORMATION" && a.Status == "ACCESS_DENIED").ToListAsync();
        Assert(logs.Count >= 2, $"Expected at least 2 ACCESS_DENIED audit entries, got {logs.Count}");

        var idorLog = logs.FirstOrDefault(l => l.UserId == "USR-PATIENT-01" && l.EntityId == patientB.Id);
        Assert(idorLog != null, "Audit log for Patient A attempting IDOR on Patient B must exist");
        Assert(idorLog!.Severity == "WARNING", $"Severity must be WARNING, got {idorLog.Severity}");
        Assert(idorLog.Status == "ACCESS_DENIED", $"Status must be ACCESS_DENIED, got {idorLog.Status}");
        Assert(idorLog.Details.Contains("Unauthorized access attempt (IDOR)"), "Details must explain IDOR attempt");
    });

    await RunCheck("SEC-PAT-VIEW-05: Doctor tra cứu hồ sơ Patient A theo đúng thẩm quyền chuyên môn -> 200 OK", async () =>
    {
        SetAuth(http, doctorToken);
        var res = await http.GetAsync($"/api/patients/{patientA.Id}");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK for Doctor, got {res.StatusCode}");

        var dto = await res.Content.ReadFromJsonAsync<PatientInformationDto>();
        Assert(dto != null && dto.Id == patientA.Id, "Doctor must receive valid Patient A DTO");
    });

    await RunCheck("SEC-PAT-VIEW-06: Receptionist tra cứu hồ sơ Patient B theo quy trình tiếp nhận -> 200 OK", async () =>
    {
        SetAuth(http, receptionistToken);
        var res = await http.GetAsync($"/api/patients/{patientB.Id}");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK for Receptionist, got {res.StatusCode}");

        var dto = await res.Content.ReadFromJsonAsync<PatientInformationDto>();
        Assert(dto != null && dto.Id == patientB.Id, "Receptionist must receive valid Patient B DTO");
    });

    await RunCheck("SEC-PAT-VIEW-07: Lab Technician tra cứu hồ sơ bệnh nhân để thực hiện xét nghiệm -> 200 OK", async () =>
    {
        SetAuth(http, labToken);
        var res = await http.GetAsync($"/api/patients/{patientA.Id}");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK for Lab Technician, got {res.StatusCode}");
    });

    await RunCheck("SEC-PAT-VIEW-08 [Audit Trail - ISO 15189]: Truy cập hợp lệ của nhân viên y tế được ghi nhận với Status=SUCCESS, Severity=INFO", async () =>
    {
        var logs = await auditLogsCollection.Find(a => a.Action == "VIEW_PATIENT_INFORMATION" && a.Status == "SUCCESS").ToListAsync();
        Assert(logs.Count >= 3, $"Expected at least 3 SUCCESS audit entries (Patient A own view, Doctor, Receptionist, Lab), got {logs.Count}");

        var docLog = logs.FirstOrDefault(l => l.UserId == "USR-DOCTOR-01");
        Assert(docLog != null, "Audit entry for Doctor view must exist");
        Assert(docLog!.Severity == "INFO", $"Severity must be INFO, got {docLog.Severity}");
        Assert(docLog.Status == "SUCCESS", $"Status must be SUCCESS, got {docLog.Status}");
    });

    await RunCheck("SEC-PAT-VIEW-09: Request không đăng nhập (Unauthenticated) gọi API tra cứu bệnh nhân -> 401 Unauthorized", async () =>
    {
        SetAuth(http, null);
        var res = await http.GetAsync($"/api/patients/{patientA.Id}");
        Assert(res.StatusCode == HttpStatusCode.Unauthorized, $"Expected 401 Unauthorized, got {res.StatusCode}");
    });

    await RunCheck("SEC-PAT-VIEW-10: Tra cứu mã bệnh nhân không tồn tại -> 404 Not Found", async () =>
    {
        SetAuth(http, doctorToken);
        var res = await http.GetAsync("/api/patients/PAT-NONEXISTENT-99999");
        Assert(res.StatusCode == HttpStatusCode.NotFound, $"Expected 404 Not Found, got {res.StatusCode}");
    });

    await RunCheck("SEC-PAT-VIEW-11 [SoD Protection]: Admin gọi API xem chi tiết bệnh nhân -> 403 Forbidden (ngăn lạm quyền xem dữ liệu lâm sàng)", async () =>
    {
        SetAuth(http, adminToken);
        var res = await http.GetAsync($"/api/patients/{patientA.Id}");
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden for Admin role, got {res.StatusCode}");
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
