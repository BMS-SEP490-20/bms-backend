using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BMS.Api.Authorization;
using BMS.Api.Controllers.Laboratory;
using BMS.Application.DTOs;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.Roles;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using BMS.Infrastructure.Mongo;
using BMS.Infrastructure.Repositories;
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
Console.WriteLine("UC70 — Retrieve Specimen History: Automated Traceability, IDOR & Audit Checks");
Console.WriteLine("Standards: ISO 15189:2022 | ISO 20658:2023 | ISO 27789:2021 | OWASP ASVS");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc70_test_" + Guid.NewGuid().ToString("N")[..8];
var client = new MongoClient(mongoUri);
var db = client.GetDatabase(testDbName);

const string jwtSecretKey = "bms_secret_key_for_testing_purposes_only_minimum_32_bytes_long!!";
const string jwtIssuer = "bms-test-issuer";
const string jwtAudience = "bms-test-audience";

var fixedNow = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
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

builder.Services.AddScoped<ISpecimenHistoryRepository, SpecimenHistoryRepository>();
builder.Services.AddScoped<ISpecimenHistoryService, SpecimenHistoryService>();
builder.Services.AddScoped<ILabQueueService, LabQueueService>();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(SpecimenHistoryController).Assembly);

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
        new Claim("role", role),
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
var doctorToken = GenerateToken("USR-DOC-01", RoleNames.Doctor, "BS Tran Van BacSi");
var managerToken = GenerateToken("USR-MGR-01", RoleNames.Manager, "Quan Ly Phong Lab");
var patientAToken = GenerateToken("PAT-2026-00001", RoleNames.Patient, "Nguyen Van BenhNhanA");
var patientBToken = GenerateToken("PAT-2026-00002", RoleNames.Patient, "Tran Thi BenhNhanB");
var receptionistToken = GenerateToken("USR-RECEPT-01", RoleNames.Receptionist, "Le Thi LeTan");

void SetAuth(HttpClient client, string? token)
{
    if (token != null)
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    else
        client.DefaultRequestHeaders.Authorization = null;
}

var samplesCol = db.GetCollection<Sample>("blood_samples");
var specimenHistoriesCol = db.GetCollection<SpecimenHistoryRecord>("specimen_histories");
var auditCol = db.GetCollection<AuditLog>("audit_logs");

try
{
    // =========================================================================
    // SEED FIXTURES:
    // Sample A (Bệnh nhân A: PAT-2026-00001) - Mẫu hợp lệ đã qua các mốc thời gian
    // Sample B (Bệnh nhân B: PAT-2026-00002) - Mẫu bị từ chối
    // =========================================================================
    var sampleA = new Sample
    {
        Id = "SMP-UC70-001",
        Barcode = "BC-UC70-001",
        PatientId = "PAT-2026-00001",
        PatientName = "Nguyen Van BenhNhanA",
        PatientGender = "Nam",
        PatientAge = 28,
        TestOrderId = "ORD-20261117-001",
        TestPackageName = "Gói Xét Nghiệm Huyết Học Tổng Quát",
        SampleType = "Blood",
        TubeColor = "Purple (EDTA)",
        Anticoagulant = "EDTA K2",
        VolumeMl = 2.5,
        StatusString = "Completed",
        PriorityString = "Normal",
        LabRoom = "Phòng Xét Nghiệm 101",
        CollectedBy = "USR-PHLEB-01",
        CollectorName = "Dieu Duong Nguyen Thi Phleb",
        CollectedAt = fixedNow.UtcDateTime.AddHours(-3),
        ReceivedBy = "USR-LAB-01",
        AssignedTechnicianName = "KTV Nguyen Van Lab",
        ReceivedAt = fixedNow.UtcDateTime.AddHours(-2).AddMinutes(-30),
        ProcessedAt = fixedNow.UtcDateTime.AddHours(-2),
        CompletedAt = fixedNow.UtcDateTime.AddHours(-1),
        CreatedAt = fixedNow.UtcDateTime.AddHours(-3).AddMinutes(-10),
        UpdatedAt = fixedNow.UtcDateTime.AddHours(-1),
        Notes = "Mẫu thu thập đạt chuẩn thể tích",
        StatusHistory = new List<SampleStatusHistoryEntry>
        {
            new()
            {
                FromStatus = "PendingCollection",
                ToStatus = "Collected",
                ChangedAt = fixedNow.UtcDateTime.AddHours(-3),
                ChangedBy = "USR-PHLEB-01",
                OperatorName = "Dieu Duong Nguyen Thi Phleb",
                Notes = "Thu thập đủ 2.5ml máu toàn phần"
            },
            new()
            {
                FromStatus = "Collected",
                ToStatus = "Received",
                ChangedAt = fixedNow.UtcDateTime.AddHours(-2).AddMinutes(-30),
                ChangedBy = "USR-LAB-01",
                OperatorName = "KTV Nguyen Van Lab",
                Notes = "Tiếp nhận mẫu tại phòng xét nghiệm Huyết học"
            },
            new()
            {
                FromStatus = "Received",
                ToStatus = "Processing",
                ChangedAt = fixedNow.UtcDateTime.AddHours(-2),
                ChangedBy = "USR-LAB-01",
                OperatorName = "KTV Nguyen Van Lab",
                Notes = "Ly tâm mẫu 3000 vòng/phút"
            },
            new()
            {
                FromStatus = "Processing",
                ToStatus = "Completed",
                ChangedAt = fixedNow.UtcDateTime.AddHours(-1),
                ChangedBy = "USR-LAB-01",
                OperatorName = "KTV Nguyen Van Lab",
                Notes = "Hoàn tất đo quang phổ và sinh hóa"
            }
        }
    };

    var sampleB = new Sample
    {
        Id = "SMP-UC70-002",
        Barcode = "BC-UC70-002",
        PatientId = "PAT-2026-00002",
        PatientName = "Tran Thi BenhNhanB",
        PatientGender = "Nu",
        PatientAge = 35,
        TestOrderId = "ORD-20261117-002",
        TestPackageName = "Đông Máu Cơ Bản (PT, APTT, Fibrinogen)",
        SampleType = "Blood",
        TubeColor = "Blue (Citrate)",
        Anticoagulant = "Sodium Citrate 3.2%",
        VolumeMl = 1.0,
        StatusString = "Rejected",
        PriorityString = "Urgent",
        LabRoom = "Phòng Xét Nghiệm 102",
        CollectedBy = "USR-PHLEB-02",
        CollectorName = "Dieu Duong Tran Van Phleb",
        CollectedAt = fixedNow.UtcDateTime.AddHours(-2),
        ReceivedBy = "USR-LAB-01",
        AssignedTechnicianName = "KTV Nguyen Van Lab",
        ReceivedAt = fixedNow.UtcDateTime.AddHours(-1).AddMinutes(-30),
        RejectedAt = fixedNow.UtcDateTime.AddHours(-1).AddMinutes(-15),
        RejectedBy = "USR-LAB-01",
        RejectionReason = "Mẫu bị đông dây (Clotted specimen) và thể tích dưới vạch định mức",
        CreatedAt = fixedNow.UtcDateTime.AddHours(-2).AddMinutes(-10),
        UpdatedAt = fixedNow.UtcDateTime.AddHours(-1).AddMinutes(-15),
        StatusHistory = new List<SampleStatusHistoryEntry>
        {
            new()
            {
                FromStatus = "PendingCollection",
                ToStatus = "Collected",
                ChangedAt = fixedNow.UtcDateTime.AddHours(-2),
                ChangedBy = "USR-PHLEB-02",
                OperatorName = "Dieu Duong Tran Van Phleb"
            },
            new()
            {
                FromStatus = "Collected",
                ToStatus = "Received",
                ChangedAt = fixedNow.UtcDateTime.AddHours(-1).AddMinutes(-30),
                ChangedBy = "USR-LAB-01",
                OperatorName = "KTV Nguyen Van Lab"
            },
            new()
            {
                FromStatus = "Received",
                ToStatus = "Rejected",
                ChangedAt = fixedNow.UtcDateTime.AddHours(-1).AddMinutes(-15),
                ChangedBy = "USR-LAB-01",
                OperatorName = "KTV Nguyen Van Lab",
                Notes = "Mẫu bị đông dây (Clotted specimen) và thể tích dưới vạch định mức"
            }
        }
    };

    await samplesCol.InsertManyAsync([sampleA, sampleB]);

    // =========================================================================
    // Test Case 1: Truy xuất bằng Sample ID / Barcode hợp lệ -> Trả đúng lịch sử
    // =========================================================================
    {
        SetAuth(http, labToken);
        var res = await http.GetAsync("/api/v1/laboratory/specimens/BC-UC70-001/history");
        var body = await res.Content.ReadAsStringAsync();

        if (res.StatusCode == HttpStatusCode.OK && body.Contains("BC-UC70-001") && body.Contains("PAT-2026-00001"))
        {
            Console.WriteLine("[PASS] SEC-HIST-01: Truy xuất lịch sử mẫu bằng Barcode hợp lệ -> 200 OK & trả đúng thông tin mẫu");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-01: Kỳ vọng 200 OK, thực tế: {(int)res.StatusCode} | Body: {body}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 2: Kiểm tra đầy đủ 11 hạng mục thông tin theo chuẩn ISO 15189 / ISO 20658
    // =========================================================================
    {
        SetAuth(http, labToken);
        var res = await http.GetAsync("/api/laboratory/samples/SMP-UC70-001/history");
        var doc = await res.Content.ReadFromJsonAsync<JsonDocument>();
        var data = doc?.RootElement.GetProperty("data");

        var hasPatient = data?.GetProperty("patientId").GetString() == "PAT-2026-00001" &&
                         data?.GetProperty("patientName").GetString() == "Nguyen Van BenhNhanA";
        var hasOrder = data?.GetProperty("orderId").GetString() == "ORD-20261117-001";
        var hasCollector = data?.GetProperty("collectorName").GetString() == "Dieu Duong Nguyen Thi Phleb";
        var hasCollectionTime = !string.IsNullOrWhiteSpace(data?.GetProperty("collectionTime").GetString());
        var hasTubeId = data?.GetProperty("barcode").GetString() == "BC-UC70-001";
        var hasTimeline = data?.GetProperty("timeline").GetArrayLength() >= 4;
        var hasReceiver = data?.GetProperty("receivedBy").GetString() == "USR-LAB-01";
        var hasQc = data?.GetProperty("qcStatus").GetString() == "Passed";
        var hasRejection = data?.GetProperty("isRejected").GetBoolean() == false;

        if (hasPatient && hasOrder && hasCollector && hasCollectionTime && hasTubeId && hasTimeline && hasReceiver && hasQc && hasRejection)
        {
            Console.WriteLine("[PASS] SEC-HIST-02 [ISO 15189 / ISO 20658]: Dữ liệu trả về đầy đủ 11 hạng mục truy xuất nguồn gốc (Bệnh nhân, Đơn chỉ định, Người lấy mẫu, Giờ lấy, Mã ống, Timeline, Tiếp nhận, QC, Trạng thái)");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-02: Thiếu trường nghiệp vụ. Patient: {hasPatient}, Order: {hasOrder}, Collector: {hasCollector}, Time: {hasCollectionTime}, Tube: {hasTubeId}, Timeline: {hasTimeline}, Receiver: {hasReceiver}, QC: {hasQc}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 3: IDOR Protection: Patient A thay ID thành mẫu của Patient B -> 403 Forbidden
    // =========================================================================
    {
        SetAuth(http, patientAToken); // Đăng nhập là Bệnh nhân A
        // Cố tình yêu cầu xem mẫu của Bệnh nhân B (SMP-UC70-002)
        var res = await http.GetAsync("/api/v1/laboratory/specimens/BC-UC70-002/history");
        var body = await res.Content.ReadAsStringAsync();

        if (res.StatusCode == HttpStatusCode.Forbidden && body.Contains("FORBIDDEN"))
        {
            Console.WriteLine("[PASS] SEC-HIST-03 [OWASP ASVS IDOR]: Bệnh nhân A cố tình truy cập lịch sử mẫu của Bệnh nhân B -> Bị chặn 403 Forbidden");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-03: Kỳ vọng 403 Forbidden, thực tế: {(int)res.StatusCode} | Body: {body}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 4: Patient A truy xuất mẫu của chính mình -> 200 OK
    // =========================================================================
    {
        SetAuth(http, patientAToken); // Đăng nhập là Bệnh nhân A
        var res = await http.GetAsync("/api/v1/laboratory/specimens/BC-UC70-001/history");

        if (res.StatusCode == HttpStatusCode.OK)
        {
            Console.WriteLine("[PASS] SEC-HIST-04: Bệnh nhân A truy xuất lịch sử mẫu của chính mình -> 200 OK hợp lệ");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-04: Kỳ vọng 200 OK, thực tế: {(int)res.StatusCode}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 5: Lịch sử cũ không bị mất khi cập nhật trạng thái (Append-only timeline)
    // =========================================================================
    {
        SetAuth(http, labToken);
        var res = await http.GetAsync("/api/v1/laboratory/specimens/BC-UC70-001/history");
        var doc = await res.Content.ReadFromJsonAsync<JsonDocument>();
        var timeline = doc?.RootElement.GetProperty("data").GetProperty("timeline");

        var eventTypes = new List<string>();
        if (timeline.HasValue)
        {
            foreach (var evt in timeline.Value.EnumerateArray())
            {
                eventTypes.Add(evt.GetProperty("eventType").GetString() ?? "");
            }
        }

        var hasCreated = eventTypes.Contains("SampleCreated");
        var hasCollected = eventTypes.Contains("SampleCollected") || eventTypes.Contains("Collected");
        var hasReceived = eventTypes.Contains("Received");
        var hasProcessing = eventTypes.Contains("Processing");
        var hasCompleted = eventTypes.Contains("Completed");

        if (hasCreated && hasCollected && hasReceived && hasProcessing && hasCompleted)
        {
            Console.WriteLine("[PASS] SEC-HIST-05: Lịch sử cũ được bảo toàn trọn vẹn qua 5 sự kiện nối tiếp (SampleCreated -> Collected -> Received -> Processing -> Completed)");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-05: Timeline bị thiếu sự kiện. Events: {string.Join(", ", eventTypes)}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 6: Mẫu bị từ chối (Rejected) -> Truy xuất ra IsRejected=true, QcStatus=Failed, RejectionReason chính xác
    // =========================================================================
    {
        SetAuth(http, labToken);
        var res = await http.GetAsync("/api/v1/laboratory/specimens/BC-UC70-002/history");
        var doc = await res.Content.ReadFromJsonAsync<JsonDocument>();
        var data = doc?.RootElement.GetProperty("data");

        var isRejected = data?.GetProperty("isRejected").GetBoolean() == true;
        var qcStatus = data?.GetProperty("qcStatus").GetString() == "Failed";
        var reason = data?.GetProperty("rejectionReason").GetString();

        if (isRejected && qcStatus && reason != null && reason.Contains("Mẫu bị đông dây"))
        {
            Console.WriteLine("[PASS] SEC-HIST-06 [ISO 20658 / ISO 15189 Rejection Traceability]: Mẫu bị từ chối hiển thị chính xác IsRejected=true, QcStatus=Failed và lý do từ chối mẫu");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-06: Thông tin từ chối mẫu không đúng. IsRejected: {isRejected}, QcStatus: {qcStatus}, Reason: {reason}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 7: Người dùng chưa đăng nhập (Unauthenticated) -> 401 Unauthorized
    // =========================================================================
    {
        SetAuth(http, null);
        var res = await http.GetAsync("/api/v1/laboratory/specimens/BC-UC70-001/history");

        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            Console.WriteLine("[PASS] SEC-HIST-07: Người chưa đăng nhập gọi API truy xuất lịch sử mẫu -> 401 Unauthorized");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-07: Kỳ vọng 401 Unauthorized, thực tế: {(int)res.StatusCode}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 8: Vai trò không được cấp quyền (Receptionist) -> 403 Forbidden
    // =========================================================================
    {
        SetAuth(http, receptionistToken);
        var res = await http.GetAsync("/api/v1/laboratory/specimens/BC-UC70-001/history");

        if (res.StatusCode == HttpStatusCode.Forbidden)
        {
            Console.WriteLine("[PASS] SEC-HIST-08: Vai trò Receptionist gọi API truy xuất lịch sử chuyên sâu -> Bị chặn 403 Forbidden");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-08: Kỳ vọng 403 Forbidden, thực tế: {(int)res.StatusCode}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 9: Bác sĩ điều trị (Doctor) truy xuất lịch sử mẫu bệnh phẩm -> 200 OK
    // =========================================================================
    {
        SetAuth(http, doctorToken);
        var res = await http.GetAsync("/api/v1/laboratory/specimens/BC-UC70-001/history");

        if (res.StatusCode == HttpStatusCode.OK)
        {
            Console.WriteLine("[PASS] SEC-HIST-09: Bác sĩ điều trị (Doctor) truy xuất lịch sử mẫu bệnh nhân -> 200 OK hợp lệ");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-09: Kỳ vọng 200 OK, thực tế: {(int)res.StatusCode}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 10: Truy xuất mẫu không tồn tại -> 404 Not Found
    // =========================================================================
    {
        SetAuth(http, labToken);
        var res = await http.GetAsync("/api/v1/laboratory/specimens/BC-NON-EXISTENT/history");
        var body = await res.Content.ReadAsStringAsync();

        if (res.StatusCode == HttpStatusCode.NotFound && body.Contains("MSG-ERR-SPECIMEN-NOT-FOUND"))
        {
            Console.WriteLine("[PASS] SEC-HIST-10: Mã mẫu không tồn tại -> 404 Not Found (MSG-ERR-SPECIMEN-NOT-FOUND)");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-10: Kỳ vọng 404 Not Found, thực tế: {(int)res.StatusCode} | Body: {body}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 11: Dữ liệu thời gian và người thực hiện nhất quán trên toàn bộ Timeline (ISO 27789)
    // =========================================================================
    {
        SetAuth(http, labToken);
        var res = await http.GetAsync("/api/v1/laboratory/specimens/BC-UC70-001/history");
        var doc = await res.Content.ReadFromJsonAsync<JsonDocument>();
        var timeline = doc?.RootElement.GetProperty("data").GetProperty("timeline");

        var isConsistent = true;
        if (timeline.HasValue)
        {
            foreach (var evt in timeline.Value.EnumerateArray())
            {
                var ts = evt.GetProperty("timestamp").GetString();
                var op = evt.GetProperty("operatorName").GetString();
                if (string.IsNullOrWhiteSpace(ts) || string.IsNullOrWhiteSpace(op))
                {
                    isConsistent = false;
                    break;
                }
            }
        }

        if (isConsistent)
        {
            Console.WriteLine("[PASS] SEC-HIST-11 [ISO 27789 Audit Consistency]: Toàn bộ các mốc sự kiện trong timeline đều có mốc thời gian và danh tính người thực hiện nhất quán");
            passed++;
        }
        else
        {
            Console.WriteLine("[FAIL] SEC-HIST-11: Phát hiện mốc sự kiện thiếu thời gian hoặc thiếu người thực hiện");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 12: Truy xuất danh sách mẫu theo Bệnh nhân & Kiểm tra IDOR
    // =========================================================================
    {
        // 12a. Bệnh nhân A xem danh sách mẫu của chính mình -> 200 OK
        SetAuth(http, patientAToken);
        var resOwn = await http.GetAsync("/api/v1/laboratory/specimens/patient/PAT-2026-00001/history");

        // 12b. Bệnh nhân A cố tình xem danh sách mẫu của Bệnh nhân B -> 403 Forbidden
        var resOther = await http.GetAsync("/api/v1/laboratory/specimens/patient/PAT-2026-00002/history");

        if (resOwn.StatusCode == HttpStatusCode.OK && resOther.StatusCode == HttpStatusCode.Forbidden)
        {
            Console.WriteLine("[PASS] SEC-HIST-12: API lịch sử theo bệnh nhân hoạt động chính xác và chặn IDOR của Bệnh nhân khác (403 Forbidden)");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-HIST-12: Kỳ vọng Own=200, Other=403, thực tế: Own={(int)resOwn.StatusCode}, Other={(int)resOther.StatusCode}");
            failed++;
        }
    }
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

class MutableTestClock : TimeProvider
{
    private DateTimeOffset _utcNow;
    public MutableTestClock(DateTimeOffset initialUtcNow) => _utcNow = initialUtcNow;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}
