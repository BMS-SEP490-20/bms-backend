using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BMS.Api.Controllers.Backup;
using BMS.Application.DTOs.AuditLogs;
using BMS.Application.DTOs.Backup;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using BMS.Infrastructure.Mongo;
using BMS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
Console.WriteLine("UC80 — Backup and Restore Data: Automated ISO & OWASP Security Checks");
Console.WriteLine("Standards: ISO/IEC 27001:2022 | ISO/IEC 25010:2023 | OWASP ASVS");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc80_test_" + Guid.NewGuid().ToString("N")[..8];
var client = new MongoClient(mongoUri);
var db = client.GetDatabase(testDbName);

const string jwtSecretKey = "bms_secret_key_for_testing_purposes_only_minimum_32_bytes_long!!";
const string jwtIssuer = "bms-test-issuer";
const string jwtAudience = "bms-test-audience";

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
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<BmsDbContext>();

builder.Services.AddSingleton<IMongoDatabase>(db);
builder.Services.AddSingleton<IMongoClient>(client);
builder.Services.AddSingleton(new MongoCollections(
    Users: "accounts",
    Roles: "roles",
    RefreshTokens: "refresh_tokens",
    ServicePackages: "service_packages",
    PasswordResetTokens: "password_reset_tokens",
    TestCriteria: "test_criteria"));

builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IBackupRepository, BackupRepository>();
builder.Services.AddScoped<IBackupService, BackupService>();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(BackupController).Assembly);

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
            ValidateLifetime = false,
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = "role"
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

// 2. Helper to generate JWT Token
string GenerateToken(string sub, string role, string name = "Test User")
{
    var tokenHandler = new JwtSecurityTokenHandler();
    var key = Encoding.UTF8.GetBytes(jwtSecretKey);
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim("sub", sub),
            new Claim(ClaimTypes.NameIdentifier, sub),
            new Claim("name", name),
            new Claim("role", role)
        }),
        Expires = DateTime.UtcNow.AddHours(2),
        Issuer = jwtIssuer,
        Audience = jwtAudience,
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return tokenHandler.WriteToken(token);
}

var tokenAdmin = GenerateToken("ADM-001", RoleNames.Admin, "Quản trị viên Hệ thống");
var tokenDoctor = GenerateToken("DOC-001", RoleNames.Doctor, "BSCKII. Nguyen Van Doctor");
var tokenLab = GenerateToken("LAB-001", RoleNames.Lab, "KTV. Tran Lab Tech");
var tokenPatient = GenerateToken("PAT-001", RoleNames.Patient, "Benh Nhan A");

// 3. Seed real operational clinical data into MongoDB
Console.WriteLine("\n[SEED] Initializing Live MongoDB Collections in " + testDbName);

var resultsCol = db.GetCollection<BsonDocument>("test_results");
var samplesCol = db.GetCollection<BsonDocument>("blood_samples");
var patientsCol = db.GetCollection<BsonDocument>("patients");
var auditCol = db.GetCollection<BsonDocument>("audit_logs");
var restoreAuditCol = db.GetCollection<BsonDocument>("restore_audit_logs");

// Insert 5 test results
var seedResults = new List<BsonDocument>();
for (int i = 1; i <= 5; i++)
{
    seedResults.Add(new BsonDocument
    {
        { "_id", $"RES-20261117-000{i}" },
        { "testResultId", $"RES-20261117-000{i}" },
        { "patient_id", $"PAT-00{i}" },
        { "status", "Approved" },
        { "validationStatus", "DoctorApproved" },
        { "created_at", DateTime.UtcNow },
        { "items", new BsonArray
            {
                new BsonDocument
                {
                    { "criterionId", "TEST-GLU" },
                    { "criterionCode", "TEST-GLU" },
                    { "measuredValue", (90 + i * 5).ToString() },
                    { "unit", "mg/dL" },
                    { "flag", "NORMAL" }
                }
            }
        }
    });
}
await resultsCol.InsertManyAsync(seedResults);

// Insert 3 blood samples
var seedSamples = new List<BsonDocument>();
for (int i = 1; i <= 3; i++)
{
    seedSamples.Add(new BsonDocument
    {
        { "_id", $"SMP-20261117-00{i}" },
        { "barcode", $"BAR-00{i}" },
        { "status", "Completed" },
        { "created_at", DateTime.UtcNow }
    });
}
await samplesCol.InsertManyAsync(seedSamples);

// Insert 2 patients
var seedPatients = new List<BsonDocument>
{
    new BsonDocument { { "_id", "PAT-001" }, { "fullname", "Nguyen Van A" }, { "phone", "0901111111" } },
    new BsonDocument { { "_id", "PAT-002" }, { "fullname", "Tran Thi B" }, { "phone", "0902222222" } }
};
await patientsCol.InsertManyAsync(seedPatients);

Console.WriteLine($"[SEED] Seeded 5 test_results, 3 blood_samples, 2 patients into {testDbName}.");

// =============================================================================
// TEST SUITE: UC80
// =============================================================================

string createdBackupId = "";

// -----------------------------------------------------------------------------
// BK-01: Thực hiện Backup -> Tạo file thành công với SHA-256 Checksum & Metadata
// -----------------------------------------------------------------------------
try
{
    var backupPayload = new CreateBackupRequestDto
    {
        BackupName = "Sao lưu dữ liệu xét nghiệm lâm sàng định kỳ",
        Scope = BackupScope.PatientTestResults,
        IncludeTestResults = true,
        IncludeBloodSamples = true,
        IncludePatientRecords = true,
        IncludeClinicalAssessments = true,
        Notes = "Sao lưu phục vụ kiểm định chất lượng theo ISO 27001"
    };

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/backup/create");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);
    req.Content = JsonContent.Create(backupPayload);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.OK)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        createdBackupId = data.GetProperty("backupId").GetString()!;
        var status = data.GetProperty("status").GetString();
        var checksum = data.GetProperty("checksumSha256").GetString();
        var fileSize = data.GetProperty("fileSizeBytes").GetInt64();
        var totalRecords = data.GetProperty("totalRecords").GetInt32();

        if (!string.IsNullOrWhiteSpace(createdBackupId) &&
            status == "Completed" &&
            checksum != null && checksum.Length == 64 &&
            fileSize > 0 &&
            totalRecords >= 10) // 5 results + 3 samples + 2 patients
        {
            Console.WriteLine($"✔ PASS [BK-01]: Thực hiện Backup thành công -> BackupId: {createdBackupId}, Records: {totalRecords}, Checksum SHA-256: {checksum[..12]}..., Size: {fileSize} bytes");
            passed++;
        }
        else
        {
            Console.WriteLine($"✖ FAIL [BK-01]: Backup output mismatch: status={status}, records={totalRecords}, checksum={checksum}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [BK-01]: Expected 200 OK, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [BK-01]: Exception: {ex.Message}");
    failed++;
}

// -----------------------------------------------------------------------------
// BK-02: Backup khi database không khả dụng -> Báo lỗi, không ghi nhận thành công giả
// -----------------------------------------------------------------------------
try
{
    // Simulate unreachable DB repository call via dedicated mock / disconnected client
    var loggerFactory = LoggerFactory.Create(b => { });
    var deadMongoContext = new MongoDbContext(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MongoDB:ConnectionString"] = "mongodb://127.0.0.1:59999", // non-existent port
            ["MongoDB:DatabaseName"] = "dead_db"
        }).Build(),
        loggerFactory.CreateLogger<MongoDbContext>()
    );
    var deadRepo = new BackupRepository(deadMongoContext, loggerFactory.CreateLogger<BackupRepository>());
    var deadService = new BackupService(deadRepo, loggerFactory.CreateLogger<BackupService>());

    var failBackupDto = new CreateBackupRequestDto
    {
        BackupName = "Bản sao lưu giả lập khi database offline",
        Scope = BackupScope.FullDatabase
    };

    var (success, msg, data) = await deadService.CreateBackupAsync(failBackupDto, "ADM-001", "Admin");

    if (!success && msg.Contains("MSG-ERR-DATABASE-UNAVAILABLE"))
    {
        Console.WriteLine("✔ PASS [BK-02]: Database không khả dụng -> Báo lỗi MSG-ERR-DATABASE-UNAVAILABLE, không ghi nhận thành công giả");
        passed++;
    }
    else
    {
        Console.WriteLine($"✖ FAIL [BK-02]: Expected failure on disconnected DB, got success={success}, msg={msg}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [BK-02]: Exception: {ex.Message}");
    failed++;
}

// -----------------------------------------------------------------------------
// BK-03: Người không có quyền Restore -> Bị từ chối với 403 Forbidden / 401 Unauthorized
// -----------------------------------------------------------------------------
try
{
    var restorePayload = new RestoreRequestDto
    {
        BackupId = createdBackupId,
        ConfirmationPhrase = "XAC_NHAN_KHOI_PHUC"
    };

    // 1. Doctor role
    var reqDoc = new HttpRequestMessage(HttpMethod.Post, $"/api/backup/{createdBackupId}/restore/execute");
    reqDoc.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenDoctor);
    reqDoc.Content = JsonContent.Create(restorePayload);
    var respDoc = await http.SendAsync(reqDoc);

    // 2. Patient role
    var reqPat = new HttpRequestMessage(HttpMethod.Post, $"/api/backup/{createdBackupId}/restore/execute");
    reqPat.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenPatient);
    reqPat.Content = JsonContent.Create(restorePayload);
    var respPat = await http.SendAsync(reqPat);

    // 3. Lab role
    var reqLab = new HttpRequestMessage(HttpMethod.Post, $"/api/backup/{createdBackupId}/restore/execute");
    reqLab.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenLab);
    reqLab.Content = JsonContent.Create(restorePayload);
    var respLab = await http.SendAsync(reqLab);

    // 4. Anonymous (no token)
    var reqAnon = new HttpRequestMessage(HttpMethod.Post, $"/api/backup/{createdBackupId}/restore/execute");
    reqAnon.Content = JsonContent.Create(restorePayload);
    var respAnon = await http.SendAsync(reqAnon);

    if (respDoc.StatusCode == HttpStatusCode.Forbidden &&
        respPat.StatusCode == HttpStatusCode.Forbidden &&
        respLab.StatusCode == HttpStatusCode.Forbidden &&
        respAnon.StatusCode == HttpStatusCode.Unauthorized)
    {
        Console.WriteLine("✔ PASS [BK-03]: Kiểm soát quyền Restore -> Doctor/Patient/Lab nhận 403 Forbidden, Anonymous nhận 401 Unauthorized");
        passed++;
    }
    else
    {
        Console.WriteLine($"✖ FAIL [BK-03]: Expected 403/401, got Doc={respDoc.StatusCode}, Pat={respPat.StatusCode}, Lab={respLab.StatusCode}, Anon={respAnon.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [BK-03]: Exception: {ex.Message}");
    failed++;
}

// -----------------------------------------------------------------------------
// BK-04 & BK-07: Thực sự thử nghiệm Restore trên môi trường & Kiểm tra dữ liệu sau Restore
// -----------------------------------------------------------------------------
try
{
    // Step A: Giả lập sự cố mất dữ liệu — Xóa sạch toàn bộ 5 kết quả xét nghiệm trong MongoDB
    var deleteRes = await resultsCol.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty);
    long countBeforeRestore = await resultsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);

    if (countBeforeRestore != 0)
    {
        Console.WriteLine($"✖ FAIL [BK-04]: Could not simulate disaster, count is {countBeforeRestore}");
        failed++;
    }
    else
    {
        Console.WriteLine($"  [SIMULATED DISASTER] Đã xóa {deleteRes.DeletedCount} bản ghi test_results. Số bản ghi hiện tại trong DB: {countBeforeRestore}");

        // Step B: Thực hiện Restore dữ liệu từ bản sao lưu createdBackupId
        var restorePayload = new RestoreRequestDto
        {
            BackupId = createdBackupId,
            ConfirmationPhrase = "XAC_NHAN_KHOI_PHUC",
            OverwriteExisting = true
        };

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/backup/{createdBackupId}/restore/execute");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);
        req.Content = JsonContent.Create(restorePayload);
        var resp = await http.SendAsync(req);

        if (resp.StatusCode == HttpStatusCode.OK)
        {
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
            var data = body.GetProperty("data");
            var restoredCount = data.GetProperty("restoredCount").GetInt32();
            var success = data.GetProperty("success").GetBoolean();
            var integrityVerified = data.GetProperty("dataIntegrityVerified").GetBoolean();

            // Step C (BK-07): Kiểm tra trực tiếp vào MongoDB để chứng minh dữ liệu đã được phục hồi
            long countAfterRestore = await resultsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
            var sampleDoc = await resultsCol.Find(Builders<BsonDocument>.Filter.Eq("_id", "RES-20261117-0001")).FirstOrDefaultAsync();

            if (success && integrityVerified && countAfterRestore == 5 && sampleDoc != null)
            {
                Console.WriteLine($"✔ PASS [BK-04]: Khôi phục dữ liệu hợp lệ thành công -> Phục hồi {restoredCount} bản ghi.");
                Console.WriteLine($"✔ PASS [BK-07]: Kiểm tra dữ liệu sau Restore -> MongoDB test_results khôi phục đủ {countAfterRestore}/5 bản ghi, bản ghi mẫu 'RES-20261117-0001' toàn vẹn.");
                passed += 2;
            }
            else
            {
                Console.WriteLine($"✖ FAIL [BK-04/BK-07]: Restore verification failed: countAfterRestore={countAfterRestore}, sampleDocExists={sampleDoc != null}");
                failed += 2;
            }
        }
        else
        {
            Console.WriteLine($"✖ FAIL [BK-04]: Expected 200 OK on execute restore, got {resp.StatusCode}");
            failed += 2;
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [BK-04/BK-07]: Exception: {ex.Message}");
    failed += 2;
}

// -----------------------------------------------------------------------------
// BK-05: Restore file bị lỗi / bị can thiệp -> Từ chối khôi phục
// -----------------------------------------------------------------------------
try
{
    // Tạo 1 bản sao lưu mới, sau đó cố tình làm hỏng status / checksum
    var corruptPayload = new CreateBackupRequestDto
    {
        BackupName = "Bản sao lưu sẽ bị can thiệp kiểm tra mã băm",
        Scope = BackupScope.PatientTestResults
    };
    var reqCreate = new HttpRequestMessage(HttpMethod.Post, "/api/backup/create");
    reqCreate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);
    reqCreate.Content = JsonContent.Create(corruptPayload);
    var respCreate = await http.SendAsync(reqCreate);
    var createJson = await respCreate.Content.ReadFromJsonAsync<JsonElement>();
    var corruptBackupId = createJson.GetProperty("data").GetProperty("backupId").GetString()!;

    // Làm hỏng tính toàn vẹn: đánh dấu status là Corrupted trong DB
    var backupsDbCol = db.GetCollection<BsonDocument>("backup_records");
    await backupsDbCol.UpdateOneAsync(
        Builders<BsonDocument>.Filter.Eq("backupId", corruptBackupId),
        Builders<BsonDocument>.Update.Set("status", (int)BackupStatus.Corrupted).Set("checksumSha256", "0000000000000000000000000000000000000000000000000000000000000000")
    );

    // Cố gắng khôi phục bản sao lưu bị lỗi
    var reqRestore = new HttpRequestMessage(HttpMethod.Post, $"/api/backup/{corruptBackupId}/restore/execute");
    reqRestore.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);
    reqRestore.Content = JsonContent.Create(new RestoreRequestDto
    {
        BackupId = corruptBackupId,
        ConfirmationPhrase = "XAC_NHAN_KHOI_PHUC"
    });
    var respRestore = await http.SendAsync(reqRestore);

    if (respRestore.StatusCode == HttpStatusCode.BadRequest)
    {
        var errBody = await respRestore.Content.ReadFromJsonAsync<JsonElement>();
        var msg = errBody.GetProperty("message").GetString()!;

        if (msg.Contains("lỗi") || msg.Contains("hỏng") || msg.Contains("tính toàn vẹn") || msg.Contains("SHA-256"))
        {
            Console.WriteLine($"✔ PASS [BK-05]: Restore file bị lỗi / sai mã băm SHA-256 -> Hệ thống từ chối khôi phục: '{msg}'");
            passed++;
        }
        else
        {
            Console.WriteLine($"✖ FAIL [BK-05]: Unexpected error message: {msg}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [BK-05]: Expected 400 BadRequest for corrupted backup, got {respRestore.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [BK-05]: Exception: {ex.Message}");
    failed++;
}

// -----------------------------------------------------------------------------
// BK-06: Restore thành công -> Có Audit Trail
// -----------------------------------------------------------------------------
try
{
    // Kiểm tra Audit Trail trong restore_audit_logs
    var restoreAudits = await restoreAuditCol.Find(Builders<BsonDocument>.Filter.Or(
        Builders<BsonDocument>.Filter.Eq("backupId", createdBackupId),
        Builders<BsonDocument>.Filter.Eq("BackupId", createdBackupId)
    )).ToListAsync();

    // Kiểm tra Audit Trail trong audit_logs
    var globalAudits = await auditCol.Find(Builders<BsonDocument>.Filter.Eq("Action", "RESTORE_DATABASE")).ToListAsync();

    if (restoreAudits.Count > 0 && globalAudits.Count > 0)
    {
        var ra = restoreAudits.First();
        var ga = globalAudits.First();
        var rId = ra.Contains("restoreId") ? ra["restoreId"].AsString : ra["RestoreId"].AsString;
        var rBy = ra.Contains("restoredBy") ? ra["restoredBy"].AsString : ra["RestoredBy"].AsString;
        var rTime = ra.Contains("executedAt") ? ra["executedAt"].ToUniversalTime() : ra["ExecutedAt"].ToUniversalTime();
        Console.WriteLine($"✔ PASS [BK-06]: Restore thành công có Audit Trail đầy đủ -> RestoreId: {rId}, Operator: {rBy}, Global Action: {ga["Action"].AsString}, Timestamp: {rTime:yyyy-MM-dd HH:mm:ss}Z");
        passed++;
    }
    else
    {
        Console.WriteLine($"✖ FAIL [BK-06]: Audit logs missing. RestoreAudits: {restoreAudits.Count}, GlobalAudits: {globalAudits.Count}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [BK-06]: Exception: {ex.Message}");
    failed++;
}

// -----------------------------------------------------------------------------
// BK-08: Pre-flight Dry Run Validation
// -----------------------------------------------------------------------------
try
{
    var req = new HttpRequestMessage(HttpMethod.Post, $"/api/backup/{createdBackupId}/restore/validate");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.OK)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        var canRestore = data.GetProperty("canRestore").GetBoolean();
        var checksumValid = data.GetProperty("checksumValid").GetBoolean();

        if (canRestore && checksumValid)
        {
            Console.WriteLine("✔ PASS [BK-08]: Pre-flight Dry Run Validation -> SHA-256 Checksum hợp lệ, sẵn sàng khôi phục an toàn");
            passed++;
        }
        else
        {
            Console.WriteLine($"✖ FAIL [BK-08]: Validation failed: canRestore={canRestore}, checksumValid={checksumValid}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [BK-08]: Expected 200 OK, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [BK-08]: Exception: {ex.Message}");
    failed++;
}

// -----------------------------------------------------------------------------
// BK-09: Confirmation Phrase Validation — Ngăn ngừa thao tác nhầm lẫn
// -----------------------------------------------------------------------------
try
{
    var wrongPhrasePayload = new RestoreRequestDto
    {
        BackupId = createdBackupId,
        ConfirmationPhrase = "SAI_CONFIRMATION_PHRASE"
    };

    var req = new HttpRequestMessage(HttpMethod.Post, $"/api/backup/{createdBackupId}/restore/execute");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);
    req.Content = JsonContent.Create(wrongPhrasePayload);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.BadRequest)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var msg = body.GetProperty("message").GetString()!;

        if (msg.Contains("Cụm từ xác nhận không chính xác"))
        {
            Console.WriteLine("✔ PASS [BK-09]: Confirmation Phrase Validation -> Cụm từ sai bị chặn với 400 Bad Request, tránh ghi đè nhầm");
            passed++;
        }
        else
        {
            Console.WriteLine($"✖ FAIL [BK-09]: Unexpected message: {msg}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [BK-09]: Expected 400 BadRequest, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [BK-09]: Exception: {ex.Message}");
    failed++;
}

// -----------------------------------------------------------------------------
// BK-10: Retention Policy — Giữ lại tối đa 7 bản sao lưu gần nhất
// -----------------------------------------------------------------------------
try
{
    // Tạo thêm 8 bản sao lưu để tổng số vượt quá 7
    for (int i = 1; i <= 8; i++)
    {
        var extraReq = new HttpRequestMessage(HttpMethod.Post, "/api/backup/create");
        extraReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);
        extraReq.Content = JsonContent.Create(new CreateBackupRequestDto
        {
            BackupName = $"Bản sao lưu lưu trữ tự động #{i}",
            Scope = BackupScope.PatientTestResults
        });
        await http.SendAsync(extraReq);
    }

    // Đếm tổng số bản sao lưu còn lại
    var reqList = new HttpRequestMessage(HttpMethod.Get, "/api/backup?pageSize=50");
    reqList.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);
    var respList = await http.SendAsync(reqList);
    var listJson = await respList.Content.ReadFromJsonAsync<JsonElement>();
    var totalBackups = listJson.GetProperty("data").GetProperty("totalCount").GetInt32();

    if (totalBackups <= 7)
    {
        Console.WriteLine($"✔ PASS [BK-10]: Retention Policy (ISO 27001) -> Tự động dọn dẹp các bản sao lưu cũ, tổng số duy trì đúng quy định: {totalBackups} <= 7 bản");
        passed++;
    }
    else
    {
        Console.WriteLine($"✖ FAIL [BK-10]: Retention limit exceeded: {totalBackups} > 7");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [BK-10]: Exception: {ex.Message}");
    failed++;
}

// 4. Cleanup and Teardown
Console.WriteLine("\n[TEARDOWN] Dropping test database " + testDbName);
await client.DropDatabaseAsync(testDbName);
await app.StopAsync();

Console.WriteLine("\n================================================================================");
Console.WriteLine($"UC80 TEST SUMMARY: {passed} PASSED, {failed} FAILED (TOTAL: {passed + failed})");
Console.WriteLine("================================================================================");

if (failed > 0) Environment.Exit(1);
