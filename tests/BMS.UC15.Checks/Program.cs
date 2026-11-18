using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;

Console.OutputEncoding = Encoding.UTF8;
MongoMappings.Register();
Console.WriteLine("================================================================================");
Console.WriteLine("UC15 — Service Package Management: Automated Security & Compliance Checks");
Console.WriteLine("Standards: OWASP ASVS (Access Control, Data Protection) | ISO 27789 | ISO 25010");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc15_test_" + Guid.NewGuid().ToString("N")[..8];
var client = new MongoClient(mongoUri);
var db = client.GetDatabase(testDbName);

const string jwtSecretKey = "bms_secret_key_for_testing_purposes_only_minimum_32_bytes_long!!";
const string jwtIssuer = "bms-test-issuer";
const string jwtAudience = "bms-test-audience";
var testClock = new MutableTestClock(DateTimeOffset.UtcNow);

// 1. Seed criteria
var testCriteriaCol = db.GetCollection<TestCriterionEntity>("test_criteria");
await testCriteriaCol.InsertManyAsync(new[]
{
    new TestCriterionEntity { CriterionCode = "GLU", CriterionName = "Đường huyết (Glucose)", IsActive = true },
    new TestCriterionEntity { CriterionCode = "CHOL", CriterionName = "Cholesterol toàn phần", IsActive = true },
    new TestCriterionEntity { CriterionCode = "TRIG", CriterionName = "Triglyceride", IsActive = true },
    new TestCriterionEntity { CriterionCode = "INACTIVE_TEST", CriterionName = "Xét nghiệm đã ngừng", IsActive = false }
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
builder.Services.AddSingleton<IServicePackageRepository, ServicePackageRepository>();
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
var managerToken = GenerateToken("USR-MGR-01", RoleNames.Manager, "Trưởng Phòng Xét Nghiệm");

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
    // UC15.1: VIEW SERVICE PACKAGE LIST
    // =========================================================================

    await RunCheck("SEC-PKG-LIST-01: Người không đăng nhập gọi API -> 401 Unauthorized", async () =>
    {
        SetAuth(http, null);
        var res = await http.GetAsync("/api/service-packages");
        Assert(res.StatusCode == HttpStatusCode.Unauthorized, $"Expected 401 Unauthorized, got {res.StatusCode}");
    });

    // Seed test packages: 1 active, 1 inactive
    var packagesRepo = app.Services.GetRequiredService<IServicePackageRepository>();
    await packagesRepo.InsertAsync(new ServicePackage
    {
        Id = "PKG-ACTIVE-01",
        Code = "PKG-ACT-01",
        Name = "Gói khám tổng quát Active",
        Price = 500000m,
        CostPrice = 250000m,
        InternalNotes = "Cấu hình bí mật nội bộ phòng xét nghiệm",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "USR-ADMIN-01",
        Tests = [new ServicePackageTest { Code = "GLU", Name = "Đường huyết" }]
    });

    await packagesRepo.InsertAsync(new ServicePackage
    {
        Id = "PKG-INACTIVE-01",
        Code = "PKG-INACT-01",
        Name = "Gói khám tạm ngừng Inactive",
        Price = 750000m,
        CostPrice = 300000m,
        InternalNotes = "Gói ngưng kinh doanh",
        IsActive = false,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "USR-ADMIN-01",
        Tests = [new ServicePackageTest { Code = "CHOL", Name = "Cholesterol" }]
    });

    await RunCheck("SEC-PKG-LIST-02: Patient xem danh sách gói công khai -> 200 OK, chỉ nhận gói đang hoạt động", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/service-packages");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        Assert(page != null && page.Items.Count > 0, "Should return items");
        Assert(page!.Items.All(p => p.IsActive), "Patient must only receive active packages");
        Assert(!page.Items.Any(p => p.Code == "PKG-INACT-01"), "Inactive package must NOT be returned to patient");
    });

    await RunCheck("SEC-PKG-LIST-03: Patient không thể xem trường nội bộ (CostPrice, InternalNotes bị ẩn)", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/service-packages");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        var activePkg = page!.Items.First(p => p.Code == "PKG-ACT-01");
        Assert(activePkg.CostPrice == null, $"CostPrice must be null for Patient, got {activePkg.CostPrice}");
        Assert(activePkg.InternalNotes == null, $"InternalNotes must be null for Patient, got {activePkg.InternalNotes}");
        Assert(activePkg.CreatedBy == null, $"CreatedBy must be null for Patient, got {activePkg.CreatedBy}");
    });

    await RunCheck("SEC-PKG-LIST-04: Admin/Manager xem danh sách -> thấy cả gói Active & Inactive và dữ liệu nội bộ", async () =>
    {
        SetAuth(http, adminToken);
        var res = await http.GetAsync("/api/service-packages");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        Assert(page!.Items.Any(p => p.Code == "PKG-INACT-01"), "Admin must see inactive packages");
        var activePkg = page.Items.First(p => p.Code == "PKG-ACT-01");
        Assert(activePkg.CostPrice == 250000m, "Admin must see CostPrice");
        Assert(activePkg.InternalNotes == "Cấu hình bí mật nội bộ phòng xét nghiệm", "Admin must see InternalNotes");
    });

    await RunCheck("SEC-PKG-LIST-05: Phân trang hoạt động đúng với tham số page và pageSize", async () =>
    {
        SetAuth(http, adminToken);
        var res = await http.GetAsync("/api/service-packages?page=1&pageSize=1");
        var page = await res.Content.ReadFromJsonAsync<PagedResult<ServicePackageDto>>();
        Assert(page!.Page == 1 && page.PageSize == 1, "Page and PageSize must match requested params");
        Assert(page.Items.Count == 1, "Should return exactly 1 item per page");
        Assert(page.TotalPages >= 2, "TotalPages must calculate correctly");
    });

    // =========================================================================
    // UC15.2: CREATE SERVICE PACKAGE
    // =========================================================================

    await RunCheck("PKG-01: Tạo gói hợp lệ -> 201 Created & Ghi Audit Trail", async () =>
    {
        SetAuth(http, managerToken);
        var req = new CreateServicePackageRequest
        {
            Code = "PKG-NEW-01",
            Name = "Gói xét nghiệm Tiểu đường & Mỡ máu",
            Description = "Xét nghiệm chỉ số đường và mỡ trong máu",
            Price = 450000m,
            CostPrice = 200000m,
            InternalNotes = "Áp dụng từ tháng 11",
            Tests =
            [
                new ServicePackageTestDto { Code = "GLU", Name = "Đường huyết" },
                new ServicePackageTestDto { Code = "CHOL", Name = "Cholesterol" }
            ]
        };

        var res = await http.PostAsJsonAsync("/api/service-packages", req);
        Assert(res.StatusCode == HttpStatusCode.Created, $"Expected 201 Created, got {res.StatusCode}");

        // Kiểm tra Audit Trail
        var auditCol = db.GetCollection<AuditLog>("audit_logs");
        var log = await auditCol.Find(a => a.Action == "SERVICE_PACKAGE_CREATED" && a.UserId == "USR-MGR-01").FirstOrDefaultAsync();
        Assert(log != null, "Audit log must be created for SERVICE_PACKAGE_CREATED");
    });

    await RunCheck("PKG-02: Tên gói rỗng -> Từ chối (400 Bad Request)", async () =>
    {
        SetAuth(http, managerToken);
        var req = new CreateServicePackageRequest
        {
            Code = "PKG-ERR-NAME",
            Name = "", // Rỗng
            Price = 200000m,
            Tests = [new ServicePackageTestDto { Code = "GLU", Name = "Đường huyết" }]
        };
        var res = await http.PostAsJsonAsync("/api/service-packages", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request, got {res.StatusCode}");
    });

    await RunCheck("PKG-03: Giá âm -> Từ chối (400 Bad Request)", async () =>
    {
        SetAuth(http, managerToken);
        var req = new CreateServicePackageRequest
        {
            Code = "PKG-ERR-PRICE",
            Name = "Gói giá âm",
            Price = -50000m, // Âm
            Tests = [new ServicePackageTestDto { Code = "GLU", Name = "Đường huyết" }]
        };
        var res = await http.PostAsJsonAsync("/api/service-packages", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request, got {res.StatusCode}");
    });

    await RunCheck("PKG-04: Test ID / Code không tồn tại trong hệ thống -> Từ chối (400 Bad Request)", async () =>
    {
        SetAuth(http, managerToken);
        var req = new CreateServicePackageRequest
        {
            Code = "PKG-ERR-TEST",
            Name = "Gói xét nghiệm có test ma",
            Price = 200000m,
            Tests = [new ServicePackageTestDto { Code = "NON_EXISTENT_TEST_CODE_999", Name = "Xét nghiệm không có thực" }]
        };
        var res = await http.PostAsJsonAsync("/api/service-packages", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request, got {res.StatusCode}");
        var body = await res.Content.ReadAsStringAsync();
        Assert(body.Contains("TEST_NOT_FOUND") || body.Contains("không tồn tại"), "Should indicate test does not exist");
    });

    await RunCheck("PKG-05: Patient gọi API tạo gói -> 403 Forbidden", async () =>
    {
        SetAuth(http, patientToken);
        var req = new CreateServicePackageRequest
        {
            Code = "PKG-HACK-01",
            Name = "Gói do Patient tự tạo trái phép",
            Price = 10000m,
            Tests = [new ServicePackageTestDto { Code = "GLU", Name = "Đường huyết" }]
        };
        var res = await http.PostAsJsonAsync("/api/service-packages", req);
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden, got {res.StatusCode}");
    });

    await RunCheck("PKG-06: Client tự gửi createdBy giả mạo -> Backend ghi nhận đúng userId từ JWT Context", async () =>
    {
        SetAuth(http, managerToken); // User is USR-MGR-01
        var jsonWithSpoofedCreatedBy = """
        {
            "code": "PKG-SPOOF-01",
            "name": "Gói giả mạo createdBy",
            "price": 300000,
            "createdBy": "USR-ADMIN-ROOT-SPOOFED",
            "tests": [
                { "code": "GLU", "name": "Đường huyết" }
            ]
        }
        """;
        var content = new StringContent(jsonWithSpoofedCreatedBy, Encoding.UTF8, "application/json");
        var res = await http.PostAsync("/api/service-packages", content);
        Assert(res.StatusCode == HttpStatusCode.Created, "Creation should succeed");

        var pkgCol = db.GetCollection<ServicePackage>("service_packages");
        var savedPkg = await pkgCol.Find(p => p.Code == "PKG-SPOOF-01").FirstOrDefaultAsync();
        Assert(savedPkg != null, "Package must be saved");
        Assert(savedPkg!.CreatedBy == "USR-MGR-01", $"CreatedBy must be 'USR-MGR-01', got '{savedPkg.CreatedBy}'");
    });

    // =========================================================================
    // UC15.3: UPDATE SERVICE PACKAGE
    // =========================================================================

    await RunCheck("SEC-PKG-UPD-01: Cập nhật tên gói hợp lệ -> 200 OK", async () =>
    {
        SetAuth(http, adminToken);
        var updateReq = new UpdateServicePackageRequest
        {
            Name = "Gói khám tổng quát Active (Đã cập nhật tên)",
            Price = 550000m,
            Tests = [new ServicePackageTestDto { Code = "GLU", Name = "Đường huyết" }]
        };
        var res = await http.PutAsJsonAsync("/api/service-packages/PKG-ACTIVE-01", updateReq);
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
    });

    await RunCheck("SEC-PKG-UPD-02: Cập nhật giá thành số âm -> Từ chối (400 Bad Request)", async () =>
    {
        SetAuth(http, adminToken);
        var updateReq = new UpdateServicePackageRequest
        {
            Name = "Gói giá âm",
            Price = -100m,
            Tests = [new ServicePackageTestDto { Code = "GLU", Name = "Đường huyết" }]
        };
        var res = await http.PutAsJsonAsync("/api/service-packages/PKG-ACTIVE-01", updateReq);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request, got {res.StatusCode}");
    });

    await RunCheck("SEC-PKG-UPD-03: Cập nhật ID không tồn tại -> 404 Not Found", async () =>
    {
        SetAuth(http, adminToken);
        var updateReq = new UpdateServicePackageRequest
        {
            Name = "Gói không tồn tại",
            Price = 100000m,
            Tests = [new ServicePackageTestDto { Code = "GLU", Name = "Đường huyết" }]
        };
        var res = await http.PutAsJsonAsync("/api/service-packages/PKG-NOT-EXIST-404", updateReq);
        Assert(res.StatusCode == HttpStatusCode.NotFound, $"Expected 404 Not Found, got {res.StatusCode}");
    });

    await RunCheck("SEC-PKG-UPD-04: Patient gọi cập nhật gói -> 403 Forbidden", async () =>
    {
        SetAuth(http, patientToken);
        var updateReq = new UpdateServicePackageRequest
        {
            Name = "Patient hack tên gói",
            Price = 1000m,
            Tests = [new ServicePackageTestDto { Code = "GLU", Name = "Đường huyết" }]
        };
        var res = await http.PutAsJsonAsync("/api/service-packages/PKG-ACTIVE-01", updateReq);
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden, got {res.StatusCode}");
    });

    await RunCheck("SEC-PKG-UPD-05: Cập nhật giá -> Audit Trail ghi nhận giá trị cũ và mới", async () =>
    {
        testClock.Advance(TimeSpan.FromSeconds(5));
        SetAuth(http, adminToken);
        var updateReq = new UpdateServicePackageRequest
        {
            Name = "Gói khám tổng quát Active (Đã cập nhật tên)",
            Price = 650000m, // Đổi từ 550,000 sang 650,000
            Tests = [new ServicePackageTestDto { Code = "GLU", Name = "Đường huyết" }]
        };
        var res = await http.PutAsJsonAsync("/api/service-packages/PKG-ACTIVE-01", updateReq);
        Assert(res.StatusCode == HttpStatusCode.OK, "Price update must succeed");

        var auditCol = db.GetCollection<AuditLog>("audit_logs");
        var log = await auditCol.Find(a => a.Action == "SERVICE_PACKAGE_UPDATED" && a.EntityId == "PKG-ACTIVE-01")
            .SortByDescending(a => a.Timestamp)
            .FirstOrDefaultAsync();
        Assert(log != null, "Audit log must exist for update");
        Assert(log!.Details.Contains("550,000") && log.Details.Contains("650,000"),
            $"Audit details must record old price 550,000 and new price 650,000, got: {log.Details}");
    });

    // =========================================================================
    // UC15.4: VIEW SERVICE PACKAGE DETAILS
    // =========================================================================

    await RunCheck("SEC-PKG-DET-01: ID hợp lệ -> Trả đúng chi tiết gói dịch vụ (200 OK)", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/service-packages/PKG-ACTIVE-01");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");
        var dto = await res.Content.ReadFromJsonAsync<ServicePackageDto>();
        Assert(dto != null && dto.Code == "PKG-ACT-01", "Should return package details");
    });

    await RunCheck("SEC-PKG-DET-02: ID không tồn tại -> 404 Not Found", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/service-packages/PKG-NON-EXISTENT");
        Assert(res.StatusCode == HttpStatusCode.NotFound, $"Expected 404 Not Found, got {res.StatusCode}");
    });

    await RunCheck("SEC-PKG-DET-03: Patient xem chi tiết -> Không thấy thông tin giá vốn (CostPrice) hay ghi chú nội bộ", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/service-packages/PKG-ACTIVE-01");
        var dto = await res.Content.ReadFromJsonAsync<ServicePackageDto>();
        Assert(dto!.CostPrice == null, "CostPrice must be null for patient");
        Assert(dto.InternalNotes == null, "InternalNotes must be null for patient");
    });

    await RunCheck("SEC-PKG-DET-04: IDOR / Gói đang Inactive -> Patient truy cập bị 404, Admin truy cập được 200", async () =>
    {
        // 1. Patient attempts to access inactive package directly by ID
        SetAuth(http, patientToken);
        var patientRes = await http.GetAsync("/api/service-packages/PKG-INACTIVE-01");
        Assert(patientRes.StatusCode == HttpStatusCode.NotFound, $"Patient must get 404 for inactive package, got {patientRes.StatusCode}");

        // 2. Admin accesses the same inactive package
        SetAuth(http, adminToken);
        var adminRes = await http.GetAsync("/api/service-packages/PKG-INACTIVE-01");
        Assert(adminRes.StatusCode == HttpStatusCode.OK, $"Admin must be allowed to access inactive package, got {adminRes.StatusCode}");
    });

    // =========================================================================
    // UC15.5: DELETE SERVICE PACKAGE
    // =========================================================================

    // Prepare a package unused and a package used in an active appointment
    await packagesRepo.InsertAsync(new ServicePackage
    {
        Id = "PKG-UNUSED-01",
        Code = "PKG-UNUSED",
        Name = "Gói chưa dùng bao giờ",
        Price = 100000m,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    });

    await packagesRepo.InsertAsync(new ServicePackage
    {
        Id = "PKG-USED-IN-ACTIVE-ORDER",
        Code = "PKG-IN-USE",
        Name = "Gói đang có lịch hẹn hoạt động",
        Price = 200000m,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    });

    var appointmentsCol = db.GetCollection<BsonDocument>("appointments");
    await appointmentsCol.InsertOneAsync(new BsonDocument
    {
        ["_id"] = "APT-ACTIVE-01",
        ["service_package_id"] = "PKG-USED-IN-ACTIVE-ORDER",
        ["status"] = "Confirmed" // Active status!
    });

    await RunCheck("PKG-DEL-01: Xóa gói chưa được sử dụng -> Xóa mềm / chuyển Inactive thành công (204 No Content)", async () =>
    {
        SetAuth(http, adminToken);
        var res = await http.DeleteAsync("/api/service-packages/PKG-UNUSED-01");
        Assert(res.StatusCode == HttpStatusCode.NoContent, $"Expected 204 NoContent, got {res.StatusCode}");

        // Confirm soft delete in database
        var rawCol = db.GetCollection<ServicePackage>("service_packages");
        var pkg = await rawCol.Find(p => p.Id == "PKG-UNUSED-01").FirstOrDefaultAsync();
        Assert(pkg != null, "Document must NOT be hard deleted from database");
        Assert(pkg!.IsDeleted, "IsDeleted must be true");
        Assert(!pkg.IsActive, "IsActive must be false");
        Assert(pkg.DeletedBy == "USR-ADMIN-01", "DeletedBy must be recorded");
    });

    await RunCheck("PKG-DEL-02: Xóa gói đang được sử dụng trong lịch hẹn hoạt động -> Bị từ chối (409 Conflict), bảo toàn lịch sử", async () =>
    {
        SetAuth(http, adminToken);
        var res = await http.DeleteAsync("/api/service-packages/PKG-USED-IN-ACTIVE-ORDER");
        Assert(res.StatusCode == HttpStatusCode.Conflict, $"Expected 409 Conflict, got {res.StatusCode}");

        // Ensure database record is untouched
        var rawCol = db.GetCollection<ServicePackage>("service_packages");
        var pkg = await rawCol.Find(p => p.Id == "PKG-USED-IN-ACTIVE-ORDER").FirstOrDefaultAsync();
        Assert(pkg != null && !pkg.IsDeleted, "Package in active use must remain untouched");
    });

    await RunCheck("PKG-DEL-03: Xóa Package ID không tồn tại -> Trả về 404 Not Found", async () =>
    {
        SetAuth(http, adminToken);
        var res = await http.DeleteAsync("/api/service-packages/PKG-404-NOT-FOUND");
        Assert(res.StatusCode == HttpStatusCode.NotFound, $"Expected 404 Not Found, got {res.StatusCode}");
    });

    await RunCheck("PKG-DEL-04: Patient gọi API Delete -> Trả về 403 Forbidden", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.DeleteAsync("/api/service-packages/PKG-ACTIVE-01");
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden, got {res.StatusCode}");
    });

    await RunCheck("PKG-DEL-05: Xóa thành công -> Ghi Audit Trail", async () =>
    {
        var auditCol = db.GetCollection<AuditLog>("audit_logs");
        var log = await auditCol.Find(a => a.Action == "SERVICE_PACKAGE_DELETED" && a.EntityId == "PKG-UNUSED-01")
            .FirstOrDefaultAsync();
        Assert(log != null, "Audit Trail must record SERVICE_PACKAGE_DELETED");
        Assert(log!.Severity == "WARNING", "Deletion severity should be WARNING");
        Assert(log.UserId == "USR-ADMIN-01", "Audit must record deleter userId");
    });

    await RunCheck("PKG-DEL-06: Gửi yêu cầu Delete nhiều lần -> Không gây lỗi dữ liệu hoặc xóa nhầm, trả về 404 an toàn", async () =>
    {
        SetAuth(http, adminToken);
        // Delete already soft-deleted package again
        var res2 = await http.DeleteAsync("/api/service-packages/PKG-UNUSED-01");
        Assert(res2.StatusCode == HttpStatusCode.NotFound, $"Repeated delete must return 404 Not Found, got {res2.StatusCode}");

        var res3 = await http.DeleteAsync("/api/service-packages/PKG-UNUSED-01");
        Assert(res3.StatusCode == HttpStatusCode.NotFound, $"Repeated delete must return 404 Not Found, got {res3.StatusCode}");
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
