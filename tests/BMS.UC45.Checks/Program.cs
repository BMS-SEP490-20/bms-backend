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
Console.WriteLine("UC45 — Update Sample Status: Automated Security, Lifecycle & Audit Checks");
Console.WriteLine("Standards: ISO 15189:2022 | ISO 20658:2023 | ISO 27789:2021 | OWASP ASVS");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc45_test_" + Guid.NewGuid().ToString("N")[..8];
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

builder.Services.AddScoped<ILabQueueService, LabQueueService>();

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
var managerToken = GenerateToken("USR-MGR-01", RoleNames.Manager, "Tran Quan Ly");
var patientToken = GenerateToken("USR-PATIENT-01", RoleNames.Patient, "Benh Nhan Hoang");

void SetAuth(HttpClient client, string? token)
{
    if (token != null)
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    else
        client.DefaultRequestHeaders.Authorization = null;
}

var samplesCol = db.GetCollection<Sample>("blood_samples");
var auditCol = db.GetCollection<AuditLog>("audit_logs");
var accountsCol = db.GetCollection<User>("accounts");

await accountsCol.InsertOneAsync(new User
{
    Id = "USR-LAB-01",
    Username = "lab01",
    FullName = "KTV Nguyen Van Lab",
    Email = "lab01@bms.local",
    Role = UserRole.Lab,
    IsActive = true
});

try
{
    // =========================================================================
    // Test Case 1: Collected -> Received -> Hợp lệ (200 OK)
    // =========================================================================
    {
        var sample1 = new Sample
        {
            Id = "SMP-UC45-001",
            Barcode = "BC-UC45-001",
            PatientId = "PAT-001",
            TestOrderId = "ORD-001",
            StatusString = SampleStatus.Collected.ToString(),
            CollectedAt = fixedNow.UtcDateTime.AddMinutes(-30),
            CreatedAt = fixedNow.UtcDateTime.AddMinutes(-30),
            StatusHistory = new List<SampleStatusHistoryEntry>
            {
                new()
                {
                    FromStatus = "PendingCollection",
                    ToStatus = "Collected",
                    ChangedAt = fixedNow.UtcDateTime.AddMinutes(-30),
                    ChangedBy = "USR-PHLEB-01",
                    OperatorName = "Dieu Duong Lay Mau"
                }
            }
        };
        await samplesCol.InsertOneAsync(sample1);

        SetAuth(http, labToken);
        var res = await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-001/status", new UpdateSampleStatusDto
        {
            Status = "Received",
            LabRoom = "Lab-101",
            Notes = "Tiếp nhận mẫu tại phòng xét nghiệm Huyết học",
            Timestamp = fixedNow.UtcDateTime.AddMinutes(-10)
        });

        var body = await res.Content.ReadAsStringAsync();
        var updated = await samplesCol.Find(s => s.Id == "SMP-UC45-001").FirstOrDefaultAsync();

        if (res.StatusCode == HttpStatusCode.OK &&
            updated != null &&
            updated.StatusString == "Received" &&
            updated.ReceivedAt.HasValue &&
            updated.ReceivedBy == "USR-LAB-01")
        {
            Console.WriteLine("[PASS] SEC-STATUS-01: Chuyển trạng thái từ Collected sang Received hợp lệ -> 200 OK & cập nhật ReceivedAt/ReceivedBy");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-01: Kỳ vọng 200 OK & Status=Received, thực tế: {(int)res.StatusCode} | Body: {body}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 2: Collected -> Completed trực tiếp -> Bị từ chối (409 Conflict)
    // =========================================================================
    {
        var sample2 = new Sample
        {
            Id = "SMP-UC45-002",
            Barcode = "BC-UC45-002",
            PatientId = "PAT-002",
            TestOrderId = "ORD-002",
            StatusString = SampleStatus.Collected.ToString(),
            CollectedAt = fixedNow.UtcDateTime.AddMinutes(-40),
            CreatedAt = fixedNow.UtcDateTime.AddMinutes(-40)
        };
        await samplesCol.InsertOneAsync(sample2);

        SetAuth(http, labToken);
        var res = await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-002/status", new UpdateSampleStatusDto
        {
            Status = "Completed",
            Notes = "Nhảy cóc từ Collected sang Completed trái phép"
        });

        var body = await res.Content.ReadAsStringAsync();

        if (res.StatusCode == HttpStatusCode.Conflict && body.Contains("INVALID_STATUS_TRANSITION"))
        {
            Console.WriteLine("[PASS] SEC-STATUS-02: Chuyển trực tiếp từ Collected sang Completed -> Bị từ chối (409 Conflict / INVALID_STATUS_TRANSITION)");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-02: Kỳ vọng 409 Conflict, thực tế: {(int)res.StatusCode} | Body: {body}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 3: Patient thay đổi trạng thái mẫu -> 403 Forbidden
    // =========================================================================
    {
        SetAuth(http, patientToken);
        var res = await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-001/status", new UpdateSampleStatusDto
        {
            Status = "Processing"
        });

        if (res.StatusCode == HttpStatusCode.Forbidden)
        {
            Console.WriteLine("[PASS] SEC-STATUS-03: Vai trò Patient gọi API cập nhật trạng thái mẫu -> Bị chặn 403 Forbidden");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-03: Kỳ vọng 403 Forbidden, thực tế: {(int)res.StatusCode}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 4: Reject nhưng không nhập lý do -> Bị từ chối (400 Bad Request)
    // =========================================================================
    {
        var sample4 = new Sample
        {
            Id = "SMP-UC45-004",
            Barcode = "BC-UC45-004",
            PatientId = "PAT-004",
            TestOrderId = "ORD-004",
            StatusString = SampleStatus.Collected.ToString(),
            CollectedAt = fixedNow.UtcDateTime.AddMinutes(-20),
            CreatedAt = fixedNow.UtcDateTime.AddMinutes(-20)
        };
        await samplesCol.InsertOneAsync(sample4);

        SetAuth(http, labToken);
        var res = await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-004/status", new UpdateSampleStatusDto
        {
            Status = "Rejected",
            RejectionReason = "   ", // khoảng trắng / rỗng
            Notes = ""
        });

        var body = await res.Content.ReadAsStringAsync();

        if (res.StatusCode == HttpStatusCode.BadRequest && body.Contains("REJECTION_REASON_REQUIRED"))
        {
            Console.WriteLine("[PASS] SEC-STATUS-04: Từ chối mẫu (Reject) nhưng không nhập lý do -> Bị từ chối (400 Bad Request / REJECTION_REASON_REQUIRED)");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-04: Kỳ vọng 400 Bad Request, thực tế: {(int)res.StatusCode} | Body: {body}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 5: Reject hợp lệ với lý do -> 200 OK & lưu RejectionReason / RejectedBy / RejectedAt
    // =========================================================================
    {
        var sample5 = new Sample
        {
            Id = "SMP-UC45-005",
            Barcode = "BC-UC45-005",
            PatientId = "PAT-005",
            TestOrderId = "ORD-005",
            StatusString = SampleStatus.Received.ToString(),
            CollectedAt = fixedNow.UtcDateTime.AddMinutes(-50),
            ReceivedAt = fixedNow.UtcDateTime.AddMinutes(-30),
            CreatedAt = fixedNow.UtcDateTime.AddMinutes(-50)
        };
        await samplesCol.InsertOneAsync(sample5);

        SetAuth(http, labToken);
        var res = await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-005/status", new UpdateSampleStatusDto
        {
            Status = "Rejected",
            RejectionReason = "Mẫu vỡ hồng cầu nặng (Gross Hemolysis 4+), không thể phân tích sinh hóa",
            Notes = "Cần lấy lại mẫu mới cho bệnh nhân"
        });

        var body = await res.Content.ReadAsStringAsync();
        var updated = await samplesCol.Find(s => s.Id == "SMP-UC45-005").FirstOrDefaultAsync();

        if (res.StatusCode == HttpStatusCode.OK &&
            updated != null &&
            updated.StatusString == "Rejected" &&
            updated.RejectionReason != null &&
            updated.RejectionReason.Contains("Mẫu vỡ hồng cầu") &&
            updated.RejectedAt.HasValue &&
            updated.RejectedBy == "USR-LAB-01")
        {
            Console.WriteLine("[PASS] SEC-STATUS-05: Từ chối mẫu hợp lệ với lý do -> 200 OK & lưu đầy đủ RejectionReason, RejectedAt, RejectedBy");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-05: Kỳ vọng 200 OK & đầy đủ trường từ chối, thực tế: {(int)res.StatusCode} | Body: {body}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 6: Cập nhật trạng thái -> Audit Trail ghi nhận trạng thái cũ và mới
    // =========================================================================
    {
        var auditEntry = await auditCol.Find(a => a.EntityId == "SMP-UC45-005" && a.Action == "REJECT_SAMPLE").FirstOrDefaultAsync();

        if (auditEntry != null &&
            auditEntry.UserId == "USR-LAB-01" &&
            auditEntry.Details.Contains("Received") &&
            auditEntry.Details.Contains("Mẫu vỡ hồng cầu") &&
            auditEntry.Severity == "WARNING")
        {
            Console.WriteLine("[PASS] SEC-STATUS-06 [ISO 27789 Audit Trail]: Audit Trail ghi nhận trạng thái cũ (Received), trạng thái mới (Rejected), lý do và người thực hiện");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-06: Audit Trail không tìm thấy hoặc thiếu dữ liệu trạng thái cũ/mới. Found: {auditEntry?.Details}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 7: Lịch sử trạng thái (StatusHistory) được nối tiếp (append-only), không xóa lịch sử cũ
    // =========================================================================
    {
        var sample7 = new Sample
        {
            Id = "SMP-UC45-007",
            Barcode = "BC-UC45-007",
            PatientId = "PAT-007",
            TestOrderId = "ORD-007",
            StatusString = SampleStatus.Collected.ToString(),
            CollectedAt = fixedNow.UtcDateTime.AddMinutes(-60),
            CreatedAt = fixedNow.UtcDateTime.AddMinutes(-60),
            StatusHistory = new List<SampleStatusHistoryEntry>
            {
                new()
                {
                    FromStatus = "PendingCollection",
                    ToStatus = "Collected",
                    ChangedAt = fixedNow.UtcDateTime.AddMinutes(-60),
                    ChangedBy = "USR-PHLEB-01",
                    OperatorName = "Phlebotomist A"
                }
            }
        };
        await samplesCol.InsertOneAsync(sample7);

        SetAuth(http, labToken);
        // Chuyển sang Received
        await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-007/status", new UpdateSampleStatusDto
        {
            Status = "Received",
            Notes = "Bước 1: Nhận mẫu"
        });

        // Chuyển tiếp sang Processing
        await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-007/status", new UpdateSampleStatusDto
        {
            Status = "Processing",
            Notes = "Bước 2: Ly tâm và tách huyết thanh"
        });

        var finalSample = await samplesCol.Find(s => s.Id == "SMP-UC45-007").FirstOrDefaultAsync();

        if (finalSample != null &&
            finalSample.StatusString == "Processing" &&
            finalSample.StatusHistory != null &&
            finalSample.StatusHistory.Count == 3 &&
            finalSample.StatusHistory[0].ToStatus == "Collected" &&
            finalSample.StatusHistory[1].ToStatus == "Received" &&
            finalSample.StatusHistory[2].ToStatus == "Processing")
        {
            Console.WriteLine("[PASS] SEC-STATUS-07: Chuỗi lịch sử vòng đời StatusHistory được bảo toàn trọn vẹn (3 bước liên tục), không bị ghi đè");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-07: StatusHistory không đúng kỳ vọng. Count: {finalSample?.StatusHistory?.Count}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 8: Mẫu đã Rejected -> Không được chuyển sang trạng thái khác (Terminal State)
    // =========================================================================
    {
        SetAuth(http, labToken);
        var res = await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-005/status", new UpdateSampleStatusDto
        {
            Status = "Received",
            Notes = "Cố tình phục hồi mẫu đã bị từ chối"
        });

        var body = await res.Content.ReadAsStringAsync();

        if (res.StatusCode == HttpStatusCode.Conflict && body.Contains("INVALID_STATUS_TRANSITION"))
        {
            Console.WriteLine("[PASS] SEC-STATUS-08: Mẫu đã bị Rejected -> Cấm chuyển sang trạng thái khác (409 Conflict), bảo vệ tính toàn vẹn dữ liệu");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-08: Kỳ vọng 409 Conflict, thực tế: {(int)res.StatusCode} | Body: {body}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 9: Mẫu đã Completed -> Cấm chuyển ngược lại Collected/Received
    // =========================================================================
    {
        var sample9 = new Sample
        {
            Id = "SMP-UC45-009",
            Barcode = "BC-UC45-009",
            PatientId = "PAT-009",
            TestOrderId = "ORD-009",
            StatusString = SampleStatus.Completed.ToString(),
            CollectedAt = fixedNow.UtcDateTime.AddHours(-2),
            ReceivedAt = fixedNow.UtcDateTime.AddHours(-1),
            CompletedAt = fixedNow.UtcDateTime.AddMinutes(-10),
            CreatedAt = fixedNow.UtcDateTime.AddHours(-2)
        };
        await samplesCol.InsertOneAsync(sample9);

        SetAuth(http, labToken);
        var res = await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-009/status", new UpdateSampleStatusDto
        {
            Status = "Received",
            Notes = "Chuyển ngược mẫu đã hoàn tất"
        });

        var body = await res.Content.ReadAsStringAsync();

        if (res.StatusCode == HttpStatusCode.Conflict && body.Contains("INVALID_STATUS_TRANSITION"))
        {
            Console.WriteLine("[PASS] SEC-STATUS-09: Mẫu đã Completed -> Cấm chuyển ngược lại Received/Collected (409 Conflict)");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-09: Kỳ vọng 409 Conflict, thực tế: {(int)res.StatusCode} | Body: {body}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 10: Sample ID không tồn tại -> 404 Not Found
    // =========================================================================
    {
        SetAuth(http, labToken);
        var res = await http.PutAsJsonAsync("/api/laboratory/samples/SMP-NON-EXISTENT/status", new UpdateSampleStatusDto
        {
            Status = "Received"
        });

        var body = await res.Content.ReadAsStringAsync();

        if (res.StatusCode == HttpStatusCode.NotFound && body.Contains("SAMPLE_NOT_FOUND"))
        {
            Console.WriteLine("[PASS] SEC-STATUS-10: Cập nhật trạng thái cho Sample ID không tồn tại -> 404 Not Found (SAMPLE_NOT_FOUND)");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-10: Kỳ vọng 404 Not Found, thực tế: {(int)res.StatusCode} | Body: {body}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 11: Người chưa đăng nhập (Unauthenticated) gọi API -> 401 Unauthorized
    // =========================================================================
    {
        SetAuth(http, null);
        var res = await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-001/status", new UpdateSampleStatusDto
        {
            Status = "Received"
        });

        if (res.StatusCode == HttpStatusCode.Unauthorized)
        {
            Console.WriteLine("[PASS] SEC-STATUS-11: Người chưa đăng nhập gọi API cập nhật trạng thái -> 401 Unauthorized");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-11: Kỳ vọng 401 Unauthorized, thực tế: {(int)res.StatusCode}");
            failed++;
        }
    }

    // =========================================================================
    // Test Case 12: Thời điểm chuyển trạng thái ở tương lai (> now) -> 400 Bad Request
    // =========================================================================
    {
        SetAuth(http, labToken);
        var res = await http.PutAsJsonAsync("/api/laboratory/samples/SMP-UC45-001/status", new UpdateSampleStatusDto
        {
            Status = "Processing",
            Timestamp = fixedNow.UtcDateTime.AddHours(2) // 2 giờ sau trong tương lai
        });

        var body = await res.Content.ReadAsStringAsync();

        if (res.StatusCode == HttpStatusCode.BadRequest && body.Contains("INVALID_SAMPLE_TIMESTAMP"))
        {
            Console.WriteLine("[PASS] SEC-STATUS-12: Thời điểm chuyển trạng thái ở tương lai -> Bị từ chối (400 Bad Request / INVALID_SAMPLE_TIMESTAMP)");
            passed++;
        }
        else
        {
            Console.WriteLine($"[FAIL] SEC-STATUS-12: Kỳ vọng 400 Bad Request, thực tế: {(int)res.StatusCode} | Body: {body}");
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
