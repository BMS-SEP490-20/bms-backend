using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BMS.Api.Authorization;
using BMS.Api.Controllers.Laboratory;
using BMS.Application.DTOs;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Infrastructure.Data;
using BMS.Infrastructure.Mongo;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;

Console.OutputEncoding = Encoding.UTF8;
MongoMappings.Register();

Console.WriteLine("================================================================================");
Console.WriteLine("UC40 — Record Sample Time: Automated Security, Pre-analytics & Audit Checks");
Console.WriteLine("Standards: ISO 15189:2022 | ISO 20658:2023 | ISO 27789:2021 | ISO 8601");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc40_test_" + Guid.NewGuid().ToString("N")[..8];
var client = new MongoClient(mongoUri);
var db = client.GetDatabase(testDbName);

const string jwtSecretKey = "bms_secret_key_for_testing_purposes_only_minimum_32_bytes_long!!";
const string jwtIssuer = "bms-test-issuer";
const string jwtAudience = "bms-test-audience";

var fixedNow = new DateTimeOffset(2026, 11, 17, 9, 30, 0, TimeSpan.Zero);
var testClock = new MutableTestClock(fixedNow);

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
builder.Services.AddSingleton<BmsDbContext>();

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

builder.Services.AddScoped<ILabQueueService, LabQueueService>();
builder.Services.AddScoped<ITestPackageRepository, BMS.Infrastructure.Repositories.TestPackageRepository>();
builder.Services.AddScoped<ITestPackageService, TestPackageService>();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(SamplesController).Assembly);

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
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

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
        expires: DateTime.UtcNow.AddMinutes(60),
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

var labToken = GenerateToken("USR-LAB-01", RoleNames.Lab, "KTV Nguyen Van Lab");
var patientToken = GenerateToken("USR-PATIENT-01", RoleNames.Patient, "Benh Nhan Hoang");

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
    // SEED DATABASE: Patient, Appointment, TestOrder, Existing Sample
    // =========================================================================
    var patientsCol = db.GetCollection<Patient>("patients");
    var appointmentsCol = db.GetCollection<BsonDocument>("appointments");
    var testOrdersCol = db.GetCollection<BsonDocument>("test_orders");
    var samplesCol = db.GetCollection<Sample>("blood_samples");
    var auditLogsCol = db.GetCollection<AuditLog>("audit_logs");

    var testPatient = new Patient
    {
        Id = "PAT-2026-00001",
        FullName = "Nguyen Van A",
        DateOfBirth = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        GenderString = "Male",
        PhoneNumber = "0901234567"
    };
    await patientsCol.InsertOneAsync(testPatient);

    // Appointment / Test Order created at 08:00 UTC today
    var orderCreatedAt = new DateTime(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc);
    var appointmentDoc = new BsonDocument
    {
        ["_id"] = "APP-20261117-01",
        ["patient_id"] = "PAT-2026-00001",
        ["status"] = "CheckedIn",
        ["created_at"] = orderCreatedAt,
        ["check_in_time"] = new DateTime(2026, 11, 17, 8, 15, 0, DateTimeKind.Utc)
    };
    await appointmentsCol.InsertOneAsync(appointmentDoc);

    var testOrderDoc = new BsonDocument
    {
        ["_id"] = "ORD-20261117-01",
        ["order_id"] = "ORD-20261117-01",
        ["appointment_id"] = "APP-20261117-01",
        ["patient_id"] = "PAT-2026-00001",
        ["created_at"] = orderCreatedAt,
        ["status"] = "Pending"
    };
    await testOrdersCol.InsertOneAsync(testOrderDoc);

    // Seed existing sample collected earlier
    var existingSample = new Sample
    {
        Id = "SMP-20261117-001",
        Barcode = "261117001",
        PatientId = "PAT-2026-00001",
        PatientName = "Nguyen Van A",
        TestOrderId = "APP-20261117-01",
        SampleType = "Blood",
        TubeColor = "Purple",
        Anticoagulant = "EDTA",
        VolumeMl = 2.0,
        StatusString = SampleStatus.Collected.ToString(),
        CollectedAt = new DateTime(2026, 11, 17, 8, 30, 0, DateTimeKind.Utc),
        CollectedBy = "USR-LAB-01",
        CollectorName = "KTV Nguyen Van Lab",
        CreatedAt = new DateTime(2026, 11, 17, 8, 35, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 11, 17, 8, 35, 0, DateTimeKind.Utc)
    };

    // Seed completed sample for terminal state test
    var completedSample = new Sample
    {
        Id = "SMP-20261117-999",
        Barcode = "261117999",
        PatientId = "PAT-2026-00001",
        TestOrderId = "APP-20261117-01",
        SampleType = "Blood",
        StatusString = SampleStatus.Completed.ToString(),
        CollectedAt = new DateTime(2026, 11, 17, 8, 30, 0, DateTimeKind.Utc),
        CreatedAt = new DateTime(2026, 11, 17, 8, 35, 0, DateTimeKind.Utc)
    };

    await samplesCol.InsertManyAsync([existingSample, completedSample]);

    // =========================================================================
    // UC40 TEST CASES
    // =========================================================================

    string newlyCreatedSampleId = "";
    var actualCollectionTime = new DateTime(2026, 11, 17, 8, 45, 0, DateTimeKind.Utc);

    await RunCheck("SEC-SAMPLE-TIME-01: Ghi nhận thời gian lấy mẫu mới hợp lệ -> 201 Created & lưu đủ các trường nghiệp vụ", async () =>
    {
        SetAuth(http, labToken);
        var req = new RecordSampleCollectionRequest
        {
            SampleId = "SMP-20261117-002",
            Barcode = "261117002",
            RfidTag = "RFID-E280-0002",
            PatientId = "PAT-2026-00001",
            AppointmentId = "APP-20261117-01",
            CollectedAt = actualCollectionTime, // 08:45 UTC
            SampleType = "Blood",
            TubeColor = "Purple",
            Anticoagulant = "EDTA",
            VolumeMl = 3.0,
            Notes = "Lấy mẫu tĩnh mạch cánh tay"
        };

        var res = await http.PostAsJsonAsync("/api/samples/record-collection", req);
        Assert(res.StatusCode == HttpStatusCode.Created, $"Expected 201 Created, got {res.StatusCode}");

        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = json.GetProperty("data");
        newlyCreatedSampleId = data.GetProperty("id").GetString()!;

        Assert(newlyCreatedSampleId == "SMP-20261117-002", "Sample ID must match");
        Assert(data.GetProperty("barcode").GetString() == "261117002", "Tube ID / Barcode must match");
        Assert(data.GetProperty("patientId").GetString() == "PAT-2026-00001", "PatientId must match");
        Assert(data.GetProperty("appointmentId").GetString() == "APP-20261117-01", "Order/AppointmentId must match");
        Assert(data.GetProperty("status").GetString() == "Collected", "Status must be Collected");
        Assert(data.GetProperty("collectedBy").GetString() == "USR-LAB-01", "CollectedBy must match authenticated user");
    });

    await RunCheck("SEC-SAMPLE-TIME-02: Phân biệt rõ CollectionTime (thời gian thực lấy mẫu) và CreatedAt (thời gian tạo dữ liệu hệ thống)", async () =>
    {
        // Query MongoDB directly
        var sampleInDb = await samplesCol.Find(s => s.Id == newlyCreatedSampleId).FirstOrDefaultAsync();
        Assert(sampleInDb != null, "Sample must exist in MongoDB blood_samples collection");

        // CollectionTime is 08:45:00 UTC (actual physical draw time)
        Assert(sampleInDb!.CollectedAt == actualCollectionTime, $"CollectedAt must be {actualCollectionTime:O}, got {sampleInDb.CollectedAt:O}");

        // CreatedAt is 09:30:00 UTC (current system time of ingestion fixedNow)
        Assert(sampleInDb.CreatedAt == fixedNow.UtcDateTime, $"CreatedAt must be {fixedNow.UtcDateTime:O}, got {sampleInDb.CreatedAt:O}");

        // They must NOT be identical: confirms differentiation between event time and system entry time
        Assert(sampleInDb.CollectedAt != sampleInDb.CreatedAt, "CollectionTime must be distinctly recorded from system CreatedAt");
    });

    await RunCheck("SEC-SAMPLE-TIME-03: Thời điểm lấy mẫu ở tương lai (> now) -> Bị từ chối (400 Bad Request / INVALID_SAMPLE_TIMESTAMP)", async () =>
    {
        SetAuth(http, labToken);
        var futureTime = fixedNow.UtcDateTime.AddHours(2); // 11:30 UTC (> 09:30 UTC)
        var req = new RecordSampleCollectionRequest
        {
            SampleId = "SMP-FUTURE-01",
            Barcode = "261117099",
            PatientId = "PAT-2026-00001",
            AppointmentId = "APP-20261117-01",
            CollectedAt = futureTime
        };

        var res = await http.PostAsJsonAsync("/api/samples/record-collection", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request for future timestamp, got {res.StatusCode}");
    });

    await RunCheck("SEC-SAMPLE-TIME-04: Thời điểm lấy mẫu không hợp lý so với thời gian đơn xét nghiệm (< order.created_at) -> 400 Bad Request", async () =>
    {
        SetAuth(http, labToken);
        // Order was created at 08:00 UTC. Try sample collection at 07:00 UTC.
        var unreasonableTime = orderCreatedAt.AddHours(-1);
        var req = new RecordSampleCollectionRequest
        {
            SampleId = "SMP-UNREASONABLE-01",
            Barcode = "261117098",
            PatientId = "PAT-2026-00001",
            AppointmentId = "APP-20261117-01",
            CollectedAt = unreasonableTime
        };

        var res = await http.PostAsJsonAsync("/api/samples/record-collection", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request for collection time before order creation, got {res.StatusCode}");
    });

    await RunCheck("SEC-SAMPLE-TIME-05: Sửa thời gian lấy mẫu của mẫu đã tồn tại -> 200 OK & Bắt buộc ghi nhận Audit Trail (ISO 27789)", async () =>
    {
        SetAuth(http, labToken);
        var updatedCollectionTime = new DateTime(2026, 11, 17, 8, 40, 0, DateTimeKind.Utc);
        var req = new RecordSampleCollectionRequest
        {
            CollectedAt = updatedCollectionTime,
            Notes = "Điều chỉnh lại thời gian lấy mẫu theo biên bản ghi nhận thực tế"
        };

        // Update via route POST /api/samples/{id}/collection-time
        var res = await http.PostAsJsonAsync($"/api/samples/{existingSample.Id}/collection-time", req);
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");

        // Verify sample updated in database
        var sampleInDb = await samplesCol.Find(s => s.Id == existingSample.Id).FirstOrDefaultAsync();
        Assert(sampleInDb != null, "Sample must exist");
        Assert(sampleInDb!.CollectedAt == updatedCollectionTime, "CollectedAt must be updated to new time");
        Assert(sampleInDb.CreatedAt == existingSample.CreatedAt, "CreatedAt must NEVER be silently changed when updating sample time");

        // Verify Audit Trail in audit_logs
        var audit = await auditLogsCol
            .Find(a => a.Action == "UPDATE_SAMPLE_COLLECTION_TIME" && a.EntityId == existingSample.Id)
            .FirstOrDefaultAsync();

        Assert(audit != null, "Audit Trail for UPDATE_SAMPLE_COLLECTION_TIME must exist");
        Assert(audit!.Status == "SUCCESS", $"Audit status must be SUCCESS, got {audit.Status}");
        Assert(audit.Severity == "INFO", $"Audit severity must be INFO, got {audit.Severity}");
        Assert(audit.Details.Contains("08:30:00") && audit.Details.Contains("08:40:00"), "Audit Trail must record both old and new collection times");
    });

    await RunCheck("SEC-SAMPLE-TIME-06: Ghi nhận thời gian cho Sample ID không tồn tại -> 404 Not Found", async () =>
    {
        SetAuth(http, labToken);
        var req = new RecordSampleCollectionRequest
        {
            CollectedAt = new DateTime(2026, 11, 17, 8, 30, 0, DateTimeKind.Utc)
        };

        var res = await http.PostAsJsonAsync("/api/samples/SMP-NONEXISTENT-9999/collection-time", req);
        Assert(res.StatusCode == HttpStatusCode.NotFound, $"Expected 404 Not Found, got {res.StatusCode}");
    });

    await RunCheck("SEC-SAMPLE-TIME-07: Người dùng vai trò Patient gọi API ghi nhận thời gian lấy mẫu -> 403 Forbidden", async () =>
    {
        SetAuth(http, patientToken);
        var req = new RecordSampleCollectionRequest
        {
            CollectedAt = new DateTime(2026, 11, 17, 8, 30, 0, DateTimeKind.Utc)
        };

        var res = await http.PostAsJsonAsync($"/api/samples/{existingSample.Id}/collection-time", req);
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden for Patient, got {res.StatusCode}");
    });

    await RunCheck("SEC-SAMPLE-TIME-08: Người chưa đăng nhập (Unauthenticated) gọi API ghi nhận thời gian lấy mẫu -> 401 Unauthorized", async () =>
    {
        SetAuth(http, null);
        var req = new RecordSampleCollectionRequest
        {
            CollectedAt = new DateTime(2026, 11, 17, 8, 30, 0, DateTimeKind.Utc)
        };

        var res = await http.PostAsJsonAsync($"/api/samples/{existingSample.Id}/collection-time", req);
        Assert(res.StatusCode == HttpStatusCode.Unauthorized, $"Expected 401 Unauthorized, got {res.StatusCode}");
    });

    await RunCheck("SEC-SAMPLE-TIME-09: Cập nhật thời gian lấy mẫu cho mẫu đã ở trạng thái hoàn tất (Completed) -> Bị từ chối (409 Conflict)", async () =>
    {
        SetAuth(http, labToken);
        var req = new RecordSampleCollectionRequest
        {
            CollectedAt = new DateTime(2026, 11, 17, 8, 40, 0, DateTimeKind.Utc)
        };

        var res = await http.PostAsJsonAsync($"/api/samples/{completedSample.Id}/collection-time", req);
        Assert(res.StatusCode == HttpStatusCode.Conflict, $"Expected 409 Conflict for terminal Completed sample, got {res.StatusCode}");
    });

    await RunCheck("SEC-SAMPLE-TIME-10 [ISO 27789 Audit Integrity]: Kiểm tra sự kiện khởi tạo mẫu được ghi nhận đầy đủ vào Audit Trail", async () =>
    {
        var audit = await auditLogsCol
            .Find(a => a.Action == "RECORD_SAMPLE_COLLECTION_TIME" && a.EntityId == newlyCreatedSampleId)
            .FirstOrDefaultAsync();

        Assert(audit != null, "Audit Trail for RECORD_SAMPLE_COLLECTION_TIME must exist");
        Assert(audit!.Status == "SUCCESS", $"Audit status must be SUCCESS, got {audit.Status}");
        Assert(!string.IsNullOrWhiteSpace(audit.AuditId) && audit.AuditId.StartsWith("AUD-"), "AuditId must follow AUD-yyyymmdd format");
        Assert(audit.TargetEntity == "Sample", "TargetEntity must be Sample");
        Assert(audit.Details.Contains("261117002"), "Details must contain tube barcode");
    });

    await RunCheck("SEC-SAMPLE-TIME-11: GET /api/laboratory/samples/staff trả danh sách nhân sự từ MongoDB (loại trừ Patient)", async () =>
    {
        var accountsCol = db.GetCollection<BsonDocument>("accounts");
        await accountsCol.InsertManyAsync(
        [
            new BsonDocument
            {
                ["_id"] = new ObjectId("670560000000000000000004"),
                ["Username"] = "tech_quochung",
                ["FullName"] = "KTV. Phạm Quốc Hưng",
                ["RoleId"] = 3,
                ["Role"] = "Lab",
                ["isActive"] = true,
                ["isDeleted"] = false
            },
            new BsonDocument
            {
                ["_id"] = new ObjectId("670560000000000000000006"),
                ["Username"] = "nurse_thimai",
                ["FullName"] = "ĐD. Lê Thị Mai",
                ["RoleId"] = 5,
                ["Role"] = "Receptionist",
                ["isActive"] = true,
                ["isDeleted"] = false
            },
            new BsonDocument
            {
                ["_id"] = new ObjectId("670560000000000000000010"),
                ["Username"] = "patient_tranvanan",
                ["FullName"] = "Trần Văn An",
                ["RoleId"] = 6,
                ["patientCode"] = BsonNull.Value,
                ["isActive"] = true,
                ["isDeleted"] = false
            }
        ]);

        SetAuth(http, labToken);
        var res = await http.GetAsync("/api/laboratory/samples/staff");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");

        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var items = json.GetProperty("data").EnumerateArray().ToList();
        Assert(items.Count == 2, $"Expected 2 staff members (excluding patient), got {items.Count}");
        Assert(items.Any(x => x.GetProperty("fullName").GetString() == "KTV. Phạm Quốc Hưng" && x.GetProperty("employeeCode").GetString() == "KTV-004"), "Lab tech with derived employeeCode KTV-004 must be present");
        Assert(items.Any(x => x.GetProperty("fullName").GetString() == "ĐD. Lê Thị Mai" && x.GetProperty("employeeCode").GetString() == "NV-REC-006"), "Nurse/Receptionist with derived employeeCode NV-REC-006 must be present");
        Assert(!items.Any(x => x.GetProperty("username").GetString() == "patient_tranvanan"), "Patient account must be excluded");
    });

    await RunCheck("SEC-SAMPLE-TIME-12: Tiếp nhận ống mẫu (Received) lưu đầy đủ Người Bàn Giao và KTV Tiếp Nhận từ CSDL", async () =>
    {
        SetAuth(http, labToken);
        var updateReq = new UpdateSampleStatusDto
        {
            Status = "Received",
            TechnicianId = "KTV-004",
            TechnicianName = "KTV. Phạm Quốc Hưng",
            HandoverById = "NV-REC-006",
            HandoverBy = "ĐD. Lê Thị Mai (Khoa Khám Bệnh / Tiếp Đón)",
            LabRoom = "LAB-ROOM-201",
            Timestamp = fixedNow.UtcDateTime
        };

        var res = await http.PutAsJsonAsync($"/api/laboratory/samples/{newlyCreatedSampleId}/status", updateReq);
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");

        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = json.GetProperty("data");
        Assert(data.GetProperty("status").GetString() == "Received", "Status must be Received");
        Assert(data.GetProperty("receivedBy").GetString() == "KTV-004", "ReceivedBy must match KTV-004");
        Assert(data.GetProperty("assignedTechnicianName").GetString() == "KTV. Phạm Quốc Hưng", "AssignedTechnicianName must match");
        Assert(data.GetProperty("handoverById").GetString() == "NV-REC-006", "HandoverById must match NV-REC-006");
        Assert(data.GetProperty("handoverBy").GetString()!.Contains("ĐD. Lê Thị Mai"), "HandoverBy must contain handover staff name");
    });

    var customPkgId = new ObjectId("670560000000000000000099");
    await RunCheck("SEC-SAMPLE-TIME-13: GET /api/v1/catalog/services hiển thị gói dịch vụ (service_packages) kèm danh sách chỉ số test", async () =>
    {
        var spCol = db.GetCollection<BsonDocument>("service_packages");
        await spCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = customPkgId,
            ["Code"] = "PKG-LIVER-TEST",
            ["Name"] = "Gói Chức Năng Gan (Kiểm thử)",
            ["Category"] = "Specialized",
            ["Price"] = new BsonDecimal128(350000m),
            ["IsActive"] = true,
            ["IsDeleted"] = false,
            ["Tests"] = new BsonArray
            {
                new BsonDocument { ["Code"] = "ALT", ["Name"] = "ALT (GPT)" },
                new BsonDocument { ["Code"] = "AST", ["Name"] = "AST (GOT)" }
            },
            ["CreatedAt"] = DateTime.UtcNow,
            ["UpdatedAt"] = DateTime.UtcNow
        });

        var critCol = db.GetCollection<BsonDocument>("test_criteria");
        await critCol.InsertManyAsync(
        [
            new BsonDocument
            {
                ["criterionCode"] = "ALT",
                ["criterionName"] = "Men gan ALT (SGPT)",
                ["unit"] = "U/L",
                ["valueType"] = "Numeric",
                ["normalMin"] = 0.0,
                ["normalMax"] = 40.0,
                ["isActive"] = true,
                ["isDeleted"] = false
            },
            new BsonDocument
            {
                ["criterionCode"] = "AST",
                ["criterionName"] = "Men gan AST (SGOT)",
                ["unit"] = "U/L",
                ["valueType"] = "Numeric",
                ["normalMin"] = 0.0,
                ["normalMax"] = 40.0,
                ["isActive"] = true,
                ["isDeleted"] = false
            }
        ]);

        SetAuth(http, labToken);
        var res = await http.GetAsync("/api/v1/catalog/services");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK from /api/packages, got {res.StatusCode}");

        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var list = json.GetProperty("packages").EnumerateArray().ToList();
        var foundPkg = list.FirstOrDefault(p => p.GetProperty("packageCode").GetString() == "PKG-LIVER-TEST");
        Assert(foundPkg.ValueKind == System.Text.Json.JsonValueKind.Object, "PKG-LIVER-TEST from service_packages must be present in /api/packages");
        var criteria = foundPkg.GetProperty("criteria").EnumerateArray().Select(c => c.GetProperty("code").GetString()).ToList();
        Assert(criteria.Count == 2 && criteria.Contains("ALT") && criteria.Contains("AST"), $"Expected [ALT, AST] criteria, got [{string.Join(", ", criteria)}]");
    });

    await RunCheck("SEC-SAMPLE-TIME-14: GET /api/laboratory/samples/{barcode} trả đúng gói dịch vụ và CHỈ các chỉ số có trong gói", async () =>
    {
        var apptOid = new ObjectId("670560000000000000000199");
        await appointmentsCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = apptOid,
            ["appointmentCode"] = "APT-LIVER-001",
            ["patientId"] = "PAT-UC40-LIVER",
            ["packageId"] = customPkgId.ToString(),
            ["status"] = "CheckedIn",
            ["createdAt"] = DateTime.UtcNow
        });

        var liverSample = new Sample
        {
            Id = "SMP-LIVER-001",
            Barcode = "SMP-LIVER-001",
            TestOrderId = apptOid.ToString(),
            PatientId = "PAT-UC40-LIVER",
            PatientName = "Nguyễn Văn Gan",
            StatusString = "Testing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await samplesCol.InsertOneAsync(liverSample);

        SetAuth(http, labToken);
        var res = await http.GetAsync("/api/laboratory/samples/SMP-LIVER-001");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");

        var json = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = json.GetProperty("data");
        Assert(data.GetProperty("testPackageName").GetString() == "Gói Chức Năng Gan (Kiểm thử)", $"Unexpected package name: {data.GetProperty("testPackageName").GetString()}");

        var pList = data.GetProperty("packageParameters").EnumerateArray().ToList();
        Assert(pList.Count == 2, $"Expected strictly 2 packageParameters (ALT, AST), got {pList.Count}");
        var codes = pList.Select(x => x.GetProperty("code").GetString()).ToList();
        Assert(codes.Contains("ALT") && codes.Contains("AST"), $"Expected ALT & AST, got [{string.Join(", ", codes)}]");
        Assert(pList.All(x => x.GetProperty("unit").GetString() == "U/L"), "Expected enriched unit U/L from test_criteria");
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
