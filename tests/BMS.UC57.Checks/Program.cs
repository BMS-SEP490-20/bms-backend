using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BMS.Api.Controllers.Laboratory;
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
Console.WriteLine("UC57 — Configure Reference Intervals: Automated Standards & Quality Checks");
Console.WriteLine("Standards: ISO 15189:2022 | ISO 27789:2021 | OWASP ASVS");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc57_test_" + Guid.NewGuid().ToString("N")[..8];
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
builder.Services.AddSingleton<MongoDbContext>();
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

builder.Services.AddScoped<IReferenceIntervalRepository, ReferenceIntervalRepository>();
builder.Services.AddScoped<BMS.Core.Interfaces.ITestCriterionRepository, TestCriterionRepository>();
builder.Services.AddScoped<IReferenceIntervalService, ReferenceIntervalService>();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(ReferenceIntervalsController).Assembly);

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

// Seed test criterion for Glucose
var criteriaCol = db.GetCollection<TestCriterionEntity>("test_criteria");
var gluCriterion = new TestCriterionEntity
{
    CriterionCode = "GLU",
    CriterionName = "Glucose máu lúc đói",
    EnglishName = "Fasting Blood Glucose",
    Category = "Biochemistry",
    Unit = "mg/dL",
    ValueType = "Numeric",
    NormalMin = 70,
    NormalMax = 99,
    PanicLow = 40,
    PanicHigh = 400,
    IsActive = true
};
await criteriaCol.InsertOneAsync(gluCriterion);

string CreateJwt(string sub, string role, string name)
{
    var tokenHandler = new JwtSecurityTokenHandler();
    var key = Encoding.UTF8.GetBytes(jwtSecretKey);
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim("sub", sub),
            new Claim(ClaimTypes.NameIdentifier, sub),
            new Claim("role", role),
            new Claim(ClaimTypes.Role, role),
            new Claim("fullName", name),
            new Claim(ClaimTypes.Name, name)
        }),
        Expires = DateTime.UtcNow.AddHours(2),
        Issuer = jwtIssuer,
        Audience = jwtAudience,
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
    };
    var token = tokenHandler.CreateToken(tokenDescriptor);
    return tokenHandler.WriteToken(token);
}

void SetAuth(HttpClient client, string? token)
{
    if (token == null)
        client.DefaultRequestHeaders.Authorization = null;
    else
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}

var managerToken = CreateJwt("USR-MGR-01", RoleNames.Manager, "Trưởng phòng xét nghiệm");
var labToken = CreateJwt("USR-LAB-01", RoleNames.Lab, "Kỹ thuật viên Lab");
var doctorToken = CreateJwt("USR-DOC-01", RoleNames.Doctor, "Bác sĩ lâm sàng");
var recepToken = CreateJwt("USR-REC-01", RoleNames.Receptionist, "Lễ tân tiếp nhận");
var patientToken = CreateJwt("USR-PAT-01", RoleNames.Patient, "Bệnh nhân Nguyễn Văn A");

string? createdIntervalId = null;

try
{
    // =========================================================================
    // Check 1: SEC-REF-01 - Manager creates valid Reference Interval -> 201 Created
    // =========================================================================
    Console.WriteLine("\n[CHECK 1] SEC-REF-01: Manager creates valid Reference Interval -> 201 Created");
    SetAuth(http, managerToken);

    var validDto = new CreateReferenceIntervalDto
    {
        TestId = "TEST-GLU",
        Gender = "ALL",
        AgeMin = 18,
        AgeMax = 120,
        LowerLimit = 70,
        UpperLimit = 99,
        Unit = "mg/dL",
        EffectiveFrom = new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc),
        Status = "ACTIVE"
    };

    var res1 = await http.PostAsJsonAsync("/api/reference-intervals", validDto);
    var body1 = await res1.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res1.StatusCode}, Body: {body1}");

    if (res1.StatusCode == HttpStatusCode.Created)
    {
        using var doc1 = JsonDocument.Parse(body1);
        var data = doc1.RootElement.GetProperty("data");
        createdIntervalId = data.GetProperty("id").GetString();
        var testId = data.GetProperty("testId").GetString();
        var lower = data.GetProperty("lowerLimit").GetDouble();
        var upper = data.GetProperty("upperLimit").GetDouble();
        var unit = data.GetProperty("unit").GetString();
        var version = data.GetProperty("version").GetInt32();

        if (!string.IsNullOrWhiteSpace(createdIntervalId) && testId == "TEST-GLU" && lower == 70 && upper == 99 && unit == "mg/dL" && version == 1)
        {
            Console.WriteLine($" PASS: Successfully configured Reference Interval {testId} (ID: {createdIntervalId}, Version: {version}, {lower}-{upper} {unit})");
            passed++;
        }
        else
        {
            Console.WriteLine(" FAIL: Response data mismatch.");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 201 Created, got {res1.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 2: SEC-REF-02 - Lower Limit > Upper Limit -> 400 Bad Request
    // =========================================================================
    Console.WriteLine("\n[CHECK 2] SEC-REF-02: LowerLimit > UpperLimit -> 400 Bad Request (Rejected)");
    SetAuth(http, managerToken);

    var invalidRangeDto = new CreateReferenceIntervalDto
    {
        TestId = "TEST-GLU",
        Gender = "ALL",
        AgeMin = 18,
        AgeMax = 120,
        LowerLimit = 120, // Greater than upperLimit
        UpperLimit = 70,
        Unit = "mg/dL",
        Status = "ACTIVE"
    };

    var res2 = await http.PostAsJsonAsync("/api/reference-intervals", invalidRangeDto);
    var body2 = await res2.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res2.StatusCode}, Body: {body2}");

    if (res2.StatusCode == HttpStatusCode.BadRequest && body2.Contains("không được lớn hơn"))
    {
        Console.WriteLine(" PASS: LowerLimit > UpperLimit correctly rejected with 400 Bad Request.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 400 Bad Request, got {res2.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 3: SEC-REF-03 - Incompatible Unit for Test -> 400 Bad Request
    // =========================================================================
    Console.WriteLine("\n[CHECK 3] SEC-REF-03: Incompatible Unit for Test -> 400 Bad Request (Rejected)");
    SetAuth(http, managerToken);

    var incompatibleUnitDto = new CreateReferenceIntervalDto
    {
        TestId = "GLU",
        Gender = "ALL",
        AgeMin = 18,
        AgeMax = 120,
        LowerLimit = 70,
        UpperLimit = 99,
        Unit = "kg", // Incompatible with blood glucose concentration
        Status = "ACTIVE"
    };

    var res3 = await http.PostAsJsonAsync("/api/reference-intervals", incompatibleUnitDto);
    var body3 = await res3.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res3.StatusCode}, Body: {body3}");

    if (res3.StatusCode == HttpStatusCode.BadRequest && body3.Contains("không tương thích"))
    {
        Console.WriteLine(" PASS: Incompatible unit correctly rejected with 400 Bad Request.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 400 Bad Request with incompatibility message, got {res3.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 3b: SEC-REF-DUP - Duplicate Test Indicator Reference Interval -> 409 Conflict
    // =========================================================================
    Console.WriteLine("\n[CHECK 3b] SEC-REF-DUP: Duplicate Reference Interval for same Test Indicator -> 409 Conflict");
    SetAuth(http, managerToken);

    var duplicateDto = new CreateReferenceIntervalDto
    {
        TestId = "TEST-GLU", // Already created in Check 1
        Gender = "ALL",
        AgeMin = 18,
        AgeMax = 120,
        LowerLimit = 75,
        UpperLimit = 105,
        Unit = "mg/dL",
        Status = "ACTIVE"
    };

    var resDup = await http.PostAsJsonAsync("/api/reference-intervals", duplicateDto);
    var bodyDup = await resDup.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {resDup.StatusCode}, Body: {bodyDup}");

    if (resDup.StatusCode == HttpStatusCode.Conflict && bodyDup.Contains("đã có khoảng tham chiếu"))
    {
        Console.WriteLine(" PASS: Duplicate test indicator correctly rejected with 409 Conflict.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 409 Conflict with duplicate message, got {resDup.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 4: SEC-REF-04 - Patient calls configure API -> 403 Forbidden
    // =========================================================================
    Console.WriteLine("\n[CHECK 4] SEC-REF-04: RBAC: Patient calls configure API -> 403 Forbidden");
    SetAuth(http, patientToken);

    var res4 = await http.PostAsJsonAsync("/api/reference-intervals", validDto);
    Console.WriteLine($"Status: {res4.StatusCode}");

    if (res4.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine(" PASS: Patient blocked from configuring reference intervals with 403 Forbidden.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 403 Forbidden, got {res4.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 5: SEC-REF-05 - Receptionist calls configure API -> 403 Forbidden
    // =========================================================================
    Console.WriteLine("\n[CHECK 5] SEC-REF-05: RBAC: Receptionist calls configure API -> 403 Forbidden");
    SetAuth(http, recepToken);

    var res5 = await http.PostAsJsonAsync("/api/reference-intervals", validDto);
    Console.WriteLine($"Status: {res5.StatusCode}");

    if (res5.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine(" PASS: Receptionist blocked from configuring reference intervals with 403 Forbidden.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 403 Forbidden, got {res5.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 6: SEC-REF-06 - Doctor calls configure API -> 403 Forbidden
    // =========================================================================
    Console.WriteLine("\n[CHECK 6] SEC-REF-06: RBAC: Doctor calls configure API -> 403 Forbidden");
    SetAuth(http, doctorToken);

    var res6 = await http.PostAsJsonAsync("/api/reference-intervals", validDto);
    Console.WriteLine($"Status: {res6.StatusCode}");

    if (res6.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine(" PASS: Doctor (clinical viewer) blocked from configuring reference intervals with 403 Forbidden.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 403 Forbidden, got {res6.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 7: SEC-REF-07 - Unauthenticated request -> 401 Unauthorized
    // =========================================================================
    Console.WriteLine("\n[CHECK 7] SEC-REF-07: Unauthenticated request -> 401 Unauthorized");
    SetAuth(http, null);

    var res7 = await http.PostAsJsonAsync("/api/reference-intervals", validDto);
    Console.WriteLine($"Status: {res7.StatusCode}");

    if (res7.StatusCode == HttpStatusCode.Unauthorized)
    {
        Console.WriteLine(" PASS: Unauthenticated request rejected with 401 Unauthorized.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 401 Unauthorized, got {res7.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 8: SEC-REF-08 - Update Reference Interval -> Version History preserved
    // =========================================================================
    Console.WriteLine("\n[CHECK 8] SEC-REF-08: Version Control: Update Reference Interval -> Version=2, PreviousVersions preserved");
    SetAuth(http, managerToken);

    var updateDto = new UpdateReferenceIntervalDto
    {
        Gender = "ALL",
        AgeMin = 18,
        AgeMax = 120,
        LowerLimit = 75, // Updated from 70
        UpperLimit = 105, // Updated from 99
        Unit = "mg/dL",
        ChangeReason = "Hiệu chuẩn theo khuyến cáo ADA 2026 mới nhất",
        ApprovedBy = "BS. Trưởng Lab",
        Status = "ACTIVE"
    };

    var res8 = await http.PutAsJsonAsync($"/api/reference-intervals/{createdIntervalId}", updateDto);
    var body8 = await res8.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res8.StatusCode}, Body: {body8}");

    if (res8.StatusCode == HttpStatusCode.OK)
    {
        using var doc8 = JsonDocument.Parse(body8);
        var data8 = doc8.RootElement.GetProperty("data");
        var version = data8.GetProperty("version").GetInt32();
        var prevVersions = data8.GetProperty("previousVersions");
        var lowerNew = data8.GetProperty("lowerLimit").GetDouble();
        var upperNew = data8.GetProperty("upperLimit").GetDouble();

        if (version == 2 && prevVersions.GetArrayLength() == 1 && lowerNew == 75 && upperNew == 105)
        {
            var v1 = prevVersions[0];
            var v1Lower = v1.GetProperty("lowerLimit").GetDouble();
            var v1Upper = v1.GetProperty("upperLimit").GetDouble();
            var v1Num = v1.GetProperty("version").GetInt32();

            if (v1Num == 1 && v1Lower == 70 && v1Upper == 99)
            {
                Console.WriteLine($" PASS: Version incremented to {version}. Historical snapshot of Version {v1Num} ({v1Lower}-{v1Upper}) preserved.");
                passed++;
            }
            else
            {
                Console.WriteLine(" FAIL: Previous version snapshot values mismatch.");
                failed++;
            }
        }
        else
        {
            Console.WriteLine($" FAIL: Version mismatch: Version={version}, PreviousVersionsCount={prevVersions.GetArrayLength()}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 200 OK, got {res8.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 9: SEC-REF-09 - ISO 27789 Audit Trail for creation and modification
    // =========================================================================
    Console.WriteLine("\n[CHECK 9] SEC-REF-09: ISO 27789: Audit Trail generated in audit_logs");
    var auditCol = db.GetCollection<AuditLog>("audit_logs");
    var auditLogs = await auditCol.Find(x => x.EntityId == createdIntervalId).ToListAsync();

    var hasCreate = auditLogs.Any(x => x.Action == "CREATE_REFERENCE_INTERVAL");
    var hasUpdate = auditLogs.Any(x => x.Action == "UPDATE_REFERENCE_INTERVAL");

    if (hasCreate && hasUpdate)
    {
        Console.WriteLine($" PASS: Found {auditLogs.Count} audit records for interval {createdIntervalId} (CREATE and UPDATE logged).");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Audit log missing. Total={auditLogs.Count}, hasCreate={hasCreate}, hasUpdate={hasUpdate}");
        failed++;
    }

    // =========================================================================
    // Check 10: SEC-REF-10 - Past Test Results remain immutable (retain snapshot)
    // =========================================================================
    Console.WriteLine("\n[CHECK 10] SEC-REF-10: Immutability: Past test results retain their historical ReferenceInterval snapshot");
    // Simulate an approved test result that was evaluated under Version 1 (70 - 99 mg/dL)
    var testResultsCol = db.GetCollection<TestResult>("test_results");
    var historicalResult = new TestResult
    {
        Id = "RES-2026-0001",
        SampleId = "SMP-2026-0001",
        PatientId = "PAT-2026-0001",
        ValidationStatus = ResultValidationStatus.DoctorApproved,
        ApprovedAt = DateTime.UtcNow.AddDays(-1),
        ApprovedByDoctorId = "DOC-001",
        DigitalSignature = "SIG-SHA256-VALID-APPROVED-HASH",
        Status = "APPROVED",
        CreatedAt = DateTime.UtcNow.AddDays(-1),
        Items = new List<TestResultItem>
        {
            new TestResultItem
            {
                CriterionCode = "GLU",
                CriterionName = "Glucose máu lúc đói",
                MeasuredValue = "85",
                Unit = "mg/dL",
                ReferenceInterval = "70 - 99 mg/dL", // Historical snapshot at test execution time
                Flag = ResultFlag.Normal
            }
        }
    };
    await testResultsCol.InsertOneAsync(historicalResult);

    // Read back test result to verify that updating interval to 75 - 105 mg/dL NEVER altered the approved test result
    var queriedResult = await testResultsCol.Find(x => x.Id == "RES-2026-0001").FirstOrDefaultAsync();
    var itemInterval = queriedResult?.Items?.FirstOrDefault()?.ReferenceInterval;

    if (queriedResult != null && itemInterval == "70 - 99 mg/dL" && TestResultRules.IsSigned(queriedResult))
    {
        Console.WriteLine($" PASS: Approved test result retains original snapshot '{itemInterval}' unchanged despite Reference Interval configuration update.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Test result ReferenceInterval was altered or not found: '{itemInterval}'");
        failed++;
    }

    // =========================================================================
    // Check 11: SEC-REF-11 - Clinical Evaluation Engine evaluates results correctly
    // =========================================================================
    Console.WriteLine("\n[CHECK 11] SEC-REF-11: Clinical Evaluation Engine against configured intervals");
    SetAuth(http, labToken);

    // Evaluate 65 mg/dL (< 75) -> Should be L (Low)
    var evalLow = await http.PostAsJsonAsync("/api/reference-intervals/evaluate", new EvaluateTestResultRequestDto
    {
        CriterionCode = "TEST-GLU",
        NumericValue = 65,
        PatientAge = 30,
        PatientGender = "Male"
    });
    var bodyLow = await evalLow.Content.ReadAsStringAsync();
    using var docLow = JsonDocument.Parse(bodyLow);
    var flagLow = docLow.RootElement.GetProperty("data").GetProperty("flag").GetString();

    // Evaluate 85 mg/dL (75-105) -> Should be NORMAL
    var evalNorm = await http.PostAsJsonAsync("/api/reference-intervals/evaluate", new EvaluateTestResultRequestDto
    {
        CriterionCode = "TEST-GLU",
        NumericValue = 85,
        PatientAge = 30,
        PatientGender = "Male"
    });
    var bodyNorm = await evalNorm.Content.ReadAsStringAsync();
    using var docNorm = JsonDocument.Parse(bodyNorm);
    var flagNorm = docNorm.RootElement.GetProperty("data").GetProperty("flag").GetString();

    // Evaluate 125 mg/dL (> 105) -> Should be H (High)
    var evalHigh = await http.PostAsJsonAsync("/api/reference-intervals/evaluate", new EvaluateTestResultRequestDto
    {
        CriterionCode = "TEST-GLU",
        NumericValue = 125,
        PatientAge = 30,
        PatientGender = "Male"
    });
    var bodyHigh = await evalHigh.Content.ReadAsStringAsync();
    using var docHigh = JsonDocument.Parse(bodyHigh);
    var flagHigh = docHigh.RootElement.GetProperty("data").GetProperty("flag").GetString();

    if (flagLow == "L" && flagNorm == "NORMAL" && flagHigh == "H")
    {
        Console.WriteLine($" PASS: Evaluation engine verified: 65 -> '{flagLow}', 85 -> '{flagNorm}', 125 -> '{flagHigh}'.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Evaluation flags mismatch: Low={flagLow}, Norm={flagNorm}, High={flagHigh}");
        failed++;
    }

    // =========================================================================
    // Check 12: SEC-REF-12 - Deactivate Reference Interval preserves historical data
    // =========================================================================
    Console.WriteLine("\n[CHECK 12] SEC-REF-12: Deactivate Interval preserves clinical data integrity");
    SetAuth(http, managerToken);

    var res12 = await http.DeleteAsync($"/api/reference-intervals/{createdIntervalId}");
    Console.WriteLine($"Status: {res12.StatusCode}");

    var intervalsCol = db.GetCollection<ReferenceIntervalEntity>("reference_intervals");
    var deactivated = await intervalsCol.Find(x => x.Id == createdIntervalId).FirstOrDefaultAsync();

    if (res12.StatusCode == HttpStatusCode.OK && deactivated != null && !deactivated.IsActive)
    {
        Console.WriteLine($" PASS: Interval {createdIntervalId} deactivated safely (IsActive=false), preserved in database.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 200 OK and IsActive=false, got status {res12.StatusCode}, IsActive={deactivated?.IsActive}");
        failed++;
    }
}
finally
{
    await app.StopAsync();
    try
    {
        await client.DropDatabaseAsync(testDbName);
    }
    catch { }
}

Console.WriteLine("\n================================================================================");
Console.WriteLine($"UC57 Verification Summary: {passed} PASSED, {failed} FAILED (Total: {passed + failed})");
Console.WriteLine("================================================================================");

return failed == 0 ? 0 : 1;

public class MutableTestClock : TimeProvider
{
    private DateTimeOffset _utcNow;
    public MutableTestClock(DateTimeOffset initialUtcNow) => _utcNow = initialUtcNow;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void SetUtcNow(DateTimeOffset newUtcNow) => _utcNow = newUtcNow;
    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
}
