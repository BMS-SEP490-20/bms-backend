using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BMS.Api.Authorization;
using BMS.Api.Controllers.Laboratory;
using BMS.Application.DTOs.AuditLogs;
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
Console.WriteLine("UC75 — Look Up Biochemical Results: Automated ISO & OWASP Security Checks");
Console.WriteLine("Standards: ISO 15189:2022 | ISO 27789:2021 | OWASP ASVS");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc75_test_" + Guid.NewGuid().ToString("N")[..8];
var client = new MongoClient(mongoUri);
var db = client.GetDatabase(testDbName);

const string jwtSecretKey = "bms_secret_key_for_testing_purposes_only_minimum_32_bytes_long!!";
const string jwtIssuer = "bms-test-issuer";
const string jwtAudience = "bms-test-audience";

var fixedNow = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

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

builder.Services.AddScoped<IPatientOwnership, PatientOwnershipRepository>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// Dummy PatientResultExportService for controller dependency
builder.Services.AddScoped<ITestReportRepository, TestReportRepository>();
builder.Services.AddScoped<ITestOrderRepository, TestOrderRepository>();
builder.Services.AddScoped<IPatientResultExportService, PatientResultExportService>();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(TestResultsController).Assembly);

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

// 3. Seed Test Data in MongoDB
Console.WriteLine("\n[SEED] Initializing MongoDB Test Data in " + testDbName);

var patientsCol = db.GetCollection<BsonDocument>("patients");
var resultsCol = db.GetCollection<BsonDocument>("test_results");
var samplesCol = db.GetCollection<BsonDocument>("blood_samples");
var auditCol = db.GetCollection<BsonDocument>("audit_logs");

// Seed Patient A (PAT-2026-00001) and Patient B (PAT-2026-00002)
await patientsCol.InsertManyAsync(new[]
{
    new BsonDocument
    {
        { "_id", "PAT-2026-00001" },
        { "patient_code", "PAT-2026-00001" },
        { "AccountId", "ACC-PATIENT-A" },
        { "fullname", "Nguyen Van A" },
        { "gender", "Nam" },
        { "phone", "0901111111" }
    },
    new BsonDocument
    {
        { "_id", "PAT-2026-00002" },
        { "patient_code", "PAT-2026-00002" },
        { "AccountId", "ACC-PATIENT-B" },
        { "fullname", "Tran Thi B" },
        { "gender", "Nu" },
        { "phone", "0902222222" }
    }
});

// Seed Blood Samples
await samplesCol.InsertManyAsync(new[]
{
    new BsonDocument
    {
        { "_id", "SMP-A01" },
        { "barcode", "BAR-A01" },
        { "patient_id", "PAT-2026-00001" },
        { "status", "Completed" }
    },
    new BsonDocument
    {
        { "_id", "SMP-B01" },
        { "barcode", "BAR-B01" },
        { "patient_id", "PAT-2026-00002" },
        { "status", "Completed" }
    }
});

// Seed Results:
// 1. RES-000001: Approved result for Patient A (Glucose = 110 mg/dL, ref: 70-99, High)
var resA1 = new BsonDocument
{
    { "_id", "RES-000001" },
    { "testResultId", "RES-000001" },
    { "patient_id", "PAT-2026-00001" },
    { "patientId", "PAT-2026-00001" },
    { "order_id", "ORD-000001" },
    { "orderId", "ORD-000001" },
    { "sample_id", "SMP-A01" },
    { "sampleId", "SMP-A01" },
    { "status", "Approved" },
    { "validationStatus", "DoctorApproved" },
    { "approved_at", DateTime.UtcNow },
    { "digital_signature", "SIG-DOC-001-A1B2C3D4E5" },
    { "analysis_run", 1 },
    { "version", 1 },
    { "items", new BsonArray
        {
            new BsonDocument
            {
                { "criterionId", "TEST-GLU" },
                { "criterionCode", "TEST-GLU" },
                { "criterionName", "Glucose" },
                { "measuredValue", "110" },
                { "unit", "mg/dL" },
                { "referenceInterval", "70-99" },
                { "flag", "H" },
                { "refLow", 70.0 },
                { "refHigh", 99.0 }
            }
        }
    },
    { "created_at", DateTime.UtcNow.AddMinutes(-30) }
};

// 2. RES-000002: Approved result for Patient B (Cholesterol = 220 mg/dL, ref: 120-200, High)
var resB1 = new BsonDocument
{
    { "_id", "RES-000002" },
    { "testResultId", "RES-000002" },
    { "patient_id", "PAT-2026-00002" },
    { "patientId", "PAT-2026-00002" },
    { "order_id", "ORD-000002" },
    { "orderId", "ORD-000002" },
    { "sample_id", "SMP-B01" },
    { "sampleId", "SMP-B01" },
    { "status", "Approved" },
    { "validationStatus", "DoctorApproved" },
    { "approved_at", DateTime.UtcNow },
    { "digital_signature", "SIG-DOC-001-F6G7H8I9J0" },
    { "analysis_run", 1 },
    { "version", 1 },
    { "items", new BsonArray
        {
            new BsonDocument
            {
                { "criterionId", "TEST-CHOL" },
                { "criterionCode", "TEST-CHOL" },
                { "criterionName", "Cholesterol Total" },
                { "measuredValue", "220" },
                { "unit", "mg/dL" },
                { "referenceInterval", "120-200" },
                { "flag", "H" },
                { "refLow", 120.0 },
                { "refHigh", 200.0 }
            }
        }
    },
    { "created_at", DateTime.UtcNow.AddMinutes(-20) }
};

// 3. RES-000003: Unapproved / Pending result for Patient A (Urea = 45 mg/dL, status = Entered)
var resA2Pending = new BsonDocument
{
    { "_id", "RES-000003" },
    { "testResultId", "RES-000003" },
    { "patient_id", "PAT-2026-00001" },
    { "patientId", "PAT-2026-00001" },
    { "order_id", "ORD-000003" },
    { "orderId", "ORD-000003" },
    { "sample_id", "SMP-A01" },
    { "sampleId", "SMP-A01" },
    { "status", "Entered" },
    { "validationStatus", "Entered" },
    { "analysis_run", 1 },
    { "version", 1 },
    { "items", new BsonArray
        {
            new BsonDocument
            {
                { "criterionId", "TEST-UREA" },
                { "criterionCode", "TEST-UREA" },
                { "criterionName", "Urea" },
                { "measuredValue", "45" },
                { "unit", "mg/dL" },
                { "referenceInterval", "15-40" },
                { "flag", "H" },
                { "refLow", 15.0 },
                { "refHigh", 40.0 }
            }
        }
    },
    { "created_at", DateTime.UtcNow.AddMinutes(-10) }
};

await resultsCol.InsertManyAsync(new[] { resA1, resB1, resA2Pending });
Console.WriteLine("[SEED] Test data inserted successfully.");

// Prepare Auth Tokens
var tokenPatientA = GenerateToken("ACC-PATIENT-A", RoleNames.Patient, "Nguyen Van A");
var tokenPatientB = GenerateToken("ACC-PATIENT-B", RoleNames.Patient, "Tran Thi B");
var tokenDoctor = GenerateToken("DOC-001", RoleNames.Doctor, "BSCKII. Nguyen Van Doctor");
var tokenLab = GenerateToken("LAB-001", RoleNames.Lab, "KTV. Tran Lab Tech");

// =============================================================================
// TEST CASES
// =============================================================================

// TEST 1: Patient A views own approved biochemical result
try
{
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/test-results/RES-000001");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenPatientA);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.OK)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        var resultId = data.GetProperty("resultId").GetString();
        var patientId = data.GetProperty("patientId").GetString();
        var orderId = data.GetProperty("orderId").GetString();
        var testId = data.GetProperty("testId").GetString();
        var val = data.GetProperty("value").GetDouble();
        var unit = data.GetProperty("unit").GetString();
        var flag = data.GetProperty("flag").GetString();
        var refRange = data.GetProperty("referenceRange");
        var refLow = refRange.GetProperty("low").GetDouble();
        var refHigh = refRange.GetProperty("high").GetDouble();

        if (resultId == "RES-000001" && patientId == "PAT-2026-00001" && orderId == "ORD-000001" &&
            testId == "TEST-GLU" && val == 110 && unit == "mg/dL" && flag == "H" && refLow == 70 && refHigh == 99)
        {
            Console.WriteLine("✔ PASS [SEC-BIO-01]: Patient views own approved biochemical result with full ISO fields");
            passed++;
        }
        else
        {
            Console.WriteLine($"✖ FAIL [SEC-BIO-01]: Field mismatch. Got: resultId={resultId}, testId={testId}, val={val}, flag={flag}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-01]: Expected 200 OK, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-01]: Exception: {ex.Message}");
    failed++;
}

// TEST 2 (CRITICAL): Anti-IDOR Protection — Patient A attempts to view Patient B's result
try
{
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/test-results/RES-000002");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenPatientA);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine("✔ PASS [SEC-BIO-02] [CRITICAL]: IDOR Attack thwarted — Patient A viewing Patient B's result correctly returns 403 Forbidden");
        passed++;
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-02]: Expected 403 Forbidden, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-02]: Exception: {ex.Message}");
    failed++;
}

// TEST 3: Parameter Tampering on query string / URL manipulation
try
{
    // Patient A tampering patientId to Patient B on /api/test-results?patientId=PAT-2026-00002
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/test-results?patientId=PAT-2026-00002");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenPatientA);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine("✔ PASS [SEC-BIO-03]: URL / Query Tampering (patientId spoofing) strictly returns 403 Forbidden");
        passed++;
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-03]: Expected 403 Forbidden, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-03]: Exception: {ex.Message}");
    failed++;
}

// TEST 4: ISO 15189:2022 — Unapproved results must NOT be released to patient as official results
try
{
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/test-results/RES-000003");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenPatientA);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine("✔ PASS [SEC-BIO-04]: ISO 15189 Release Rule — Patient viewing unapproved result returns 403 Forbidden");
        passed++;
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-04]: Expected 403 Forbidden for unapproved result, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-04]: Exception: {ex.Message}");
    failed++;
}

// TEST 5: Doctor views biochemical result within clinical scope
try
{
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/test-results/RES-000001");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenDoctor);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.OK)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        if (data.GetProperty("patientId").GetString() == "PAT-2026-00001")
        {
            Console.WriteLine("✔ PASS [SEC-BIO-05]: Doctor successfully views biochemical results within clinical scope");
            passed++;
        }
        else
        {
            Console.WriteLine("✖ FAIL [SEC-BIO-05]: PatientId mismatch in doctor response");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-05]: Expected 200 OK, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-05]: Exception: {ex.Message}");
    failed++;
}

// TEST 6: Lab Technician views in-progress/unapproved result for verification
try
{
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/test-results/RES-000003");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenLab);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.OK)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        var status = data.GetProperty("status").GetString();
        var isOfficial = data.GetProperty("isOfficialRelease").GetBoolean();

        if (status == "Entered" && !isOfficial)
        {
            Console.WriteLine("✔ PASS [SEC-BIO-06]: Lab Technician can view in-progress result with unapproved flag");
            passed++;
        }
        else
        {
            Console.WriteLine($"✖ FAIL [SEC-BIO-06]: Status unexpected: {status}, isOfficialRelease: {isOfficial}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-06]: Expected 200 OK for Lab Technician, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-06]: Exception: {ex.Message}");
    failed++;
}

// TEST 7: ISO 15189 Immutability — Direct silent modification of approved result is rejected
try
{
    var updatePayload = new { notes = "Tampered notes" };
    var req = new HttpRequestMessage(HttpMethod.Put, "/api/test-results/RES-000001");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenLab);
    req.Content = JsonContent.Create(updatePayload);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.Conflict)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var code = body.GetProperty("code").GetString();
        if (code == "RESULT_SIGNED_READ_ONLY")
        {
            Console.WriteLine("✔ PASS [SEC-BIO-07]: ISO 15189 Immutability — Silent edit of signed result blocked with 409 Conflict (RESULT_SIGNED_READ_ONLY)");
            passed++;
        }
        else
        {
            Console.WriteLine($"✖ FAIL [SEC-BIO-07]: Expected code RESULT_SIGNED_READ_ONLY, got {code}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-07]: Expected 409 Conflict, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-07]: Exception: {ex.Message}");
    failed++;
}

// TEST 8: ISO 15189 Rerun Workflow — Creates new Analysis Run & preserves historical run
string newRerunResultId = "";
try
{
    var rerunPayload = new RerunBiochemicalTestRequestDto
    {
        Reason = "Kiểm tra lại chỉ số Glucose nghi ngờ sai lệch máy quang phổ",
        EquipmentCode = "EQUIP-BIO-02",
        Notes = "Chạy lại lần 2 bằng hóa chất lô mới",
        Parameters = new List<RerunBiochemicalParameterDto>
        {
            new RerunBiochemicalParameterDto
            {
                Code = "TEST-GLU",
                Name = "Glucose",
                Value = 108.5,
                Unit = "mg/dL",
                RefLow = 70.0,
                RefHigh = 99.0
            }
        }
    };

    var req = new HttpRequestMessage(HttpMethod.Post, "/api/test-results/RES-000001/rerun");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenLab);
    req.Content = JsonContent.Create(rerunPayload);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.OK)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        newRerunResultId = data.GetProperty("resultId").GetString()!;
        var runNum = data.GetProperty("analysisRun").GetInt32();
        var verNum = data.GetProperty("version").GetInt32();
        var parentId = data.GetProperty("parentResultId").GetString();

        // Check that original result in DB is preserved (still exists, marked superseded)
        var originalDoc = await resultsCol.Find(Builders<BsonDocument>.Filter.Eq("_id", "RES-000001")).FirstOrDefaultAsync();
        var originalItems = originalDoc["items"].AsBsonArray;
        var originalVal = originalItems[0]["measuredValue"].AsString;
        var isSuperseded = originalDoc.Contains("is_superseded") && originalDoc["is_superseded"].AsBoolean;

        if (runNum == 2 && verNum == 2 && parentId == "RES-000001" && originalVal == "110" && isSuperseded)
        {
            Console.WriteLine("✔ PASS [SEC-BIO-08]: ISO 15189 Rerun Workflow — Analysis Run 2 created; historical run preserved intact");
            passed++;
        }
        else
        {
            Console.WriteLine($"✖ FAIL [SEC-BIO-08]: Rerun mismatch: run={runNum}, ver={verNum}, originalVal={originalVal}, superseded={isSuperseded}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-08]: Expected 200 OK, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-08]: Exception: {ex.Message}");
    failed++;
}

// TEST 9: ISO 27789 Audit Trail — Verify access and rerun events logged
try
{
    var auditLogs = await auditCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
    var lookupLogs = auditLogs.Where(l => l.Contains("Action") && l["Action"].AsString == "LOOKUP_BIOCHEMICAL_RESULT").ToList();
    var rerunLogs = auditLogs.Where(l => l.Contains("Action") && l["Action"].AsString == "RERUN_BIOCHEMICAL_TEST").ToList();

    if (lookupLogs.Count > 0 && rerunLogs.Count > 0)
    {
        Console.WriteLine($"✔ PASS [SEC-BIO-09]: ISO 27789 Audit Trail verified — Recorded {lookupLogs.Count} lookup events and {rerunLogs.Count} rerun events");
        passed++;
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-09]: Expected audit logs not found. Total logs: {auditLogs.Count}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-09]: Exception: {ex.Message}");
    failed++;
}

// TEST 10: OWASP ASVS Authentication Check — Unauthenticated request rejected with 401
try
{
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/test-results/RES-000001");
    // No Authorization header
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.Unauthorized)
    {
        Console.WriteLine("✔ PASS [SEC-BIO-10]: OWASP ASVS Auth — Anonymous request correctly rejected with 401 Unauthorized");
        passed++;
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-10]: Expected 401 Unauthorized, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-10]: Exception: {ex.Message}");
    failed++;
}

// TEST 11: Targeted biomarker query param filter (?testId=TEST-GLU)
try
{
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/test-results/RES-000001?testId=TEST-GLU");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenPatientA);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.OK)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var data = body.GetProperty("data");
        var testId = data.GetProperty("testId").GetString();

        if (testId == "TEST-GLU")
        {
            Console.WriteLine("✔ PASS [SEC-BIO-11]: Targeted biomarker query parameter (?testId=TEST-GLU) works correctly");
            passed++;
        }
        else
        {
            Console.WriteLine($"✖ FAIL [SEC-BIO-11]: Expected testId TEST-GLU, got {testId}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-11]: Expected 200 OK, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-11]: Exception: {ex.Message}");
    failed++;
}

// TEST 12: Patient Result List scoping — Patient only sees approved results of their own profile
try
{
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/test-results?patientId=PAT-2026-00001");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenPatientA);
    var resp = await http.SendAsync(req);

    if (resp.StatusCode == HttpStatusCode.OK)
    {
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var total = body.GetProperty("total").GetInt32();
        var dataArr = body.GetProperty("data");

        // Out of RES-000001 (Approved), RES-000003 (Entered), and RES-000002 (Patient B):
        // Patient A should only receive 1 released result (RES-000001)
        if (total == 1 && dataArr.GetArrayLength() == 1)
        {
            var firstId = dataArr[0].GetProperty("resultId").GetString();
            if (firstId == "RES-000001")
            {
                Console.WriteLine("✔ PASS [SEC-BIO-12]: Patient Result List scoping — Only approved results for Patient A returned; others filtered");
                passed++;
            }
            else
            {
                Console.WriteLine($"✖ FAIL [SEC-BIO-12]: Unexpected result in list: {firstId}");
                failed++;
            }
        }
        else
        {
            Console.WriteLine($"✖ FAIL [SEC-BIO-12]: Expected total 1, got total={total}, items={dataArr.GetArrayLength()}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($"✖ FAIL [SEC-BIO-12]: Expected 200 OK, got {resp.StatusCode}");
        failed++;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✖ FAIL [SEC-BIO-12]: Exception: {ex.Message}");
    failed++;
}

// 4. Cleanup and Teardown
Console.WriteLine("\n[TEARDOWN] Dropping test database " + testDbName);
await client.DropDatabaseAsync(testDbName);
await app.StopAsync();

Console.WriteLine("\n================================================================================");
Console.WriteLine($"UC75 TEST SUMMARY: {passed} PASSED, {failed} FAILED (TOTAL: {passed + failed})");
Console.WriteLine("================================================================================");

if (failed > 0) Environment.Exit(1);
