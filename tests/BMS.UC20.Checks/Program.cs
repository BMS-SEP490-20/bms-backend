using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BMS.Api.Controllers.ServicePackages;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.ServicePackages;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Constants;
using BMS.Core.Entities;
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
Console.WriteLine("UC20 — Search Test Packages: Automated Security & Compliance Checks");
Console.WriteLine("Standards: OWASP ASVS (Input Validation, Anti-NoSQLi) | ISO 25010 Efficiency");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc20_test_" + Guid.NewGuid().ToString("N")[..8];
var client = new MongoClient(mongoUri);
var db = client.GetDatabase(testDbName);

const string jwtSecretKey = "bms_secret_key_for_testing_purposes_only_minimum_32_bytes_long!!";
const string jwtIssuer = "bms-test-issuer";
const string jwtAudience = "bms-test-audience";
var testClock = new MutableTestClock(DateTimeOffset.UtcNow);

// 1. Seed packages for searching
var packagesRepo = new ServicePackageRepository(db, new MongoCollections(
    Users: "accounts",
    Roles: "roles",
    RefreshTokens: "refresh_tokens",
    ServicePackages: "service_packages",
    PasswordResetTokens: "password_reset_tokens",
    TestCriteria: "test_criteria"));

await packagesRepo.InsertAsync(new ServicePackage
{
    Id = "PKG-SEARCH-01",
    Code = "PKG-GEN-01",
    Name = "Gói xét nghiệm Tổng quát Nam Nữ",
    Description = "Đánh giá chức năng gan, thận, đường huyết và mỡ máu toàn diện",
    Price = 600000m,
    CostPrice = 300000m,
    InternalNotes = "Cấu hình mật: SecretAdminConfigPassword123",
    IsActive = true,
    CreatedAt = DateTime.UtcNow,
    CreatedBy = "USR-ADMIN-01",
    Tests =
    [
        new ServicePackageTest { Code = "GLU", Name = "Đường huyết (Glucose)" },
        new ServicePackageTest { Code = "CHOL", Name = "Cholesterol toàn phần" }
    ]
});

await packagesRepo.InsertAsync(new ServicePackage
{
    Id = "PKG-SEARCH-02",
    Code = "PKG-CARDIO-01",
    Name = "Gói tầm soát Tim mạch Nâng cao",
    Description = "Tầm soát nguy cơ đột quỵ, xơ vữa động mạch",
    Price = 1200000m,
    CostPrice = 700000m,
    InternalNotes = "Lưu ý nội bộ tim mạch",
    IsActive = true,
    CreatedAt = DateTime.UtcNow,
    CreatedBy = "USR-ADMIN-01",
    Tests =
    [
        new ServicePackageTest { Code = "TRIG", Name = "Triglyceride huyết tương" }
    ]
});

await packagesRepo.InsertAsync(new ServicePackage
{
    Id = "PKG-SEARCH-03",
    Code = "PKG-INACTIVE-01",
    Name = "Gói khám Tổng quát cũ Inactive",
    Description = "Gói đã ngừng cung cấp cho bệnh nhân",
    Price = 400000m,
    CostPrice = 200000m,
    InternalNotes = "Gói cũ",
    IsActive = false, // INACTIVE!
    CreatedAt = DateTime.UtcNow,
    CreatedBy = "USR-ADMIN-01",
    Tests =
    [
        new ServicePackageTest { Code = "GLU", Name = "Đường huyết (Glucose)" }
    ]
});

// 2. Set up WebApplication Host
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
builder.Services.AddSingleton<IServicePackageRepository>(packagesRepo);
builder.Services.AddSingleton<BMS.Core.Interfaces.IAuditLogRepository, AuditLogRepository>();
builder.Services.AddSingleton<BMS.Core.Interfaces.ITestCriterionRepository, TestCriterionRepository>();
builder.Services.AddScoped<IServicePackageService, ServicePackageService>();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(ServicePackagesController).Assembly);

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

var patientToken = GenerateToken("USR-PATIENT-01", RoleNames.Patient, "Nguyen Van Bệnh Nhân");
var adminToken = GenerateToken("USR-ADMIN-01", RoleNames.Admin, "Quản Trị Viên");

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
    // UC20: SEARCH TEST PACKAGES
    // =========================================================================

    await RunCheck("SEC-SEARCH-01: Tìm theo tên gói -> Trả về kết quả chính xác", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/service-packages/search?keyword=" + Uri.EscapeDataString("Tổng quát"));
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        Assert(page != null && page.TotalItems >= 1, "Should find at least 1 package");
        Assert(page!.Items.Any(p => p.Code == "PKG-GEN-01"), "Should match 'PKG-GEN-01'");
    });

    await RunCheck("SEC-SEARCH-02: Tìm theo mã xét nghiệm con trong gói (GLU) -> Kết quả chính xác", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/service-packages/search?keyword=GLU");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        Assert(page != null && page.TotalItems >= 1, "Should find packages containing GLU");
        Assert(page!.Items.Any(p => p.Code == "PKG-GEN-01"), "Should match 'PKG-GEN-01'");
    });

    await RunCheck("SEC-SEARCH-03: Tìm từ khóa không tồn tại -> Danh sách rỗng (TotalItems = 0)", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/service-packages/search?keyword=XYZ_NON_EXISTENT_PACKAGE_99999");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        Assert(page != null && page.TotalItems == 0 && page.Items.Count == 0, "Should return empty list for non-existent keyword");
    });

    await RunCheck("SEC-SEARCH-04: Nhập chuỗi dài bất thường (> 100 ký tự) -> Hệ thống từ chối (400 Bad Request)", async () =>
    {
        SetAuth(http, patientToken);
        var longKeyword = new string('A', 105);
        var res = await http.GetAsync($"/api/service-packages/search?keyword={longKeyword}");
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request for keyword > 100 chars, got {res.StatusCode}");
    });

    await RunCheck("SEC-SEARCH-05: Nhập payload NoSQL Injection {\"$ne\": null} -> Coi như chuỗi ký tự thường, không thay đổi logic truy vấn", async () =>
    {
        SetAuth(http, patientToken);
        var injectionPayload = Uri.EscapeDataString("{\"$ne\": null}");
        var res = await http.GetAsync($"/api/service-packages/search?keyword={injectionPayload}");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        // Payload must NOT bypass filters or match everything
        Assert(page != null && page.TotalItems == 0, "NoSQL injection payload must return 0 results and not match all packages");
    });

    await RunCheck("SEC-SEARCH-06: Nhập payload Regex / ReDoS (.*.*.*.* hoặc ký tự đặc biệt) -> Được escape an toàn, không match toàn bộ DB", async () =>
    {
        SetAuth(http, patientToken);
        var regexPayload = Uri.EscapeDataString(".*.*.*.*");
        var res = await http.GetAsync($"/api/service-packages/search?keyword={regexPayload}");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        Assert(page != null && page.TotalItems == 0, "Regex wildcard must be escaped and not match all records");
    });

    await RunCheck("SEC-SEARCH-07: Người dùng Patient không thể tìm kiếm theo dữ liệu nội bộ (InternalNotes)", async () =>
    {
        SetAuth(http, patientToken);
        // "SecretAdminConfigPassword123" is only in InternalNotes of PKG-GEN-01
        var res = await http.GetAsync("/api/service-packages/search?keyword=SecretAdminConfigPassword123");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        Assert(page != null && page.TotalItems == 0, "InternalNotes must NOT be searchable, should return 0 items");
    });

    await RunCheck("SEC-SEARCH-08: Người dùng Patient tìm kiếm chỉ thấy gói đang hoạt động (IsActive == true), không thấy gói Inactive", async () =>
    {
        SetAuth(http, patientToken);
        // Both PKG-GEN-01 (Active) and PKG-INACTIVE-01 (Inactive) have "Tổng quát" in Name
        var res = await http.GetAsync("/api/service-packages/search?keyword=" + Uri.EscapeDataString("Tổng quát"));
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        Assert(page != null && page.Items.All(p => p.IsActive), "Patient must only receive active packages");
        Assert(!page.Items.Any(p => p.Code == "PKG-INACTIVE-01"), "Inactive package must NOT be returned to patient");
    });

    await RunCheck("SEC-SEARCH-09: Dữ liệu trả về cho Patient không chứa trường nhạy cảm nội bộ (CostPrice, InternalNotes bị ẩn)", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/service-packages/search?keyword=" + Uri.EscapeDataString("Tim mạch"));
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        Assert(page != null && page.Items.Count > 0, "Should find Tim mach package");
        var pkg = page!.Items.First();
        Assert(pkg.CostPrice == null, $"CostPrice must be null for patient, got {pkg.CostPrice}");
        Assert(pkg.InternalNotes == null, $"InternalNotes must be null for patient, got {pkg.InternalNotes}");
        Assert(pkg.CreatedBy == null, $"CreatedBy must be null for patient, got {pkg.CreatedBy}");
    });

    await RunCheck("SEC-SEARCH-10: Người chưa đăng nhập gọi API Search -> Trả về 401 Unauthorized", async () =>
    {
        SetAuth(http, null);
        var res = await http.GetAsync("/api/service-packages/search?keyword=" + Uri.EscapeDataString("Tổng quát"));
        Assert(res.StatusCode == HttpStatusCode.Unauthorized, $"Expected 401 Unauthorized, got {res.StatusCode}");
    });

    await RunCheck("SEC-SEARCH-11: Giới hạn phân trang: PageSize vượt quá 100 (e.g. 150) -> Bị từ chối (400 Bad Request)", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/service-packages/search?keyword=Tim&pageSize=150");
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request for pageSize > 100, got {res.StatusCode}");
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
