using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BMS.Api.Authorization;
using BMS.Api.Controllers.Notifications;
using BMS.Application.DTOs.Notifications;
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
using MongoDB.Bson;
using MongoDB.Driver;

Console.OutputEncoding = Encoding.UTF8;
MongoMappings.Register();

Console.WriteLine("================================================================================");
Console.WriteLine("UC60 — Notify Test Results: Automated Security, Workflow & Audit Checks");
Console.WriteLine("Standards: ISO 15189:2022 | ISO 27789:2021 | OWASP ASVS");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc60_test_" + Guid.NewGuid().ToString("N")[..8];
var client = new MongoClient(mongoUri);
var db = client.GetDatabase(testDbName);

const string jwtSecretKey = "bms_secret_key_for_testing_purposes_only_minimum_32_bytes_long!!";
const string jwtIssuer = "bms-test-issuer";
const string jwtAudience = "bms-test-audience";

var fixedNow = new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
var testClock = new MutableTestClock(fixedNow);
var testEmailService = new TestEmailService();

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

builder.Services.AddSingleton<IEmailService>(testEmailService);
builder.Services.AddScoped<ITestResultNotificationRepository, TestResultNotificationRepository>();
builder.Services.AddScoped<IPatientOwnership, PatientOwnershipRepository>();
builder.Services.AddScoped<ITestResultNotificationService, TestResultNotificationService>();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(TestResultNotificationsController).Assembly);

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

// Seed Patients
var patientsCol = db.GetCollection<Patient>("patients");
var p1 = new Patient
{
    Id = "PAT-2026-0001",
    PatientCode = "PAT-2026-0001",
    FullName = "Nguyen Van A",
    DateOfBirth = new DateTime(1990, 5, 12, 0, 0, 0, DateTimeKind.Utc),
    Gender = BiologicalGender.Male,
    PhoneNumber = "0901234567",
    Email = "patient1@example.com",
    AccountId = "USR-PATIENT-01",
    CreatedAt = DateTime.UtcNow
};
var p2 = new Patient
{
    Id = "PAT-2026-0002",
    PatientCode = "PAT-2026-0002",
    FullName = "Tran Thi B",
    DateOfBirth = new DateTime(1995, 8, 20, 0, 0, 0, DateTimeKind.Utc),
    Gender = BiologicalGender.Female,
    PhoneNumber = "0909876543",
    Email = "patient2@example.com",
    AccountId = "USR-PATIENT-02",
    CreatedAt = DateTime.UtcNow
};
await patientsCol.InsertManyAsync(new[] { p1, p2 });

// Seed Test Results: 1 Approved (ORD-APP-01), 1 Pending / Not Approved (ORD-PEND-02)
var testResultsCol = db.GetCollection<TestResult>("test_results");
var approvedResult = new TestResult
{
    Id = "RES-APP-001",
    TestResultId = "RES-APP-001",
    AppointmentId = "ORD-APP-01",
    SampleId = "SMP-APP-01",
    PatientId = "PAT-2026-0001",
    ValidationStatus = ResultValidationStatus.DoctorApproved,
    ApprovedAt = DateTime.UtcNow,
    ApprovedByDoctorId = "DOC-001",
    DigitalSignature = "SIG-VALID-DOCTOR-APPROVED-HASH",
    Status = "APPROVED",
    CreatedAt = DateTime.UtcNow,
    Items = new List<TestResultItem>
    {
        new TestResultItem
        {
            CriterionCode = "GLU",
            CriterionName = "Glucose máu",
            MeasuredValue = "85",
            Unit = "mg/dL",
            ReferenceInterval = "70 - 99 mg/dL",
            Flag = ResultFlag.Normal
        }
    }
};

var pendingResult = new TestResult
{
    Id = "RES-PEND-002",
    TestResultId = "RES-PEND-002",
    AppointmentId = "ORD-PEND-02",
    SampleId = "SMP-PEND-02",
    PatientId = "PAT-2026-0001",
    ValidationStatus = ResultValidationStatus.TechnicianVerified, // NOT DoctorApproved yet
    ApprovedAt = null,
    DigitalSignature = null,
    Status = "VERIFIED",
    CreatedAt = DateTime.UtcNow,
    Items = new List<TestResultItem>
    {
        new TestResultItem
        {
            CriterionCode = "GLU",
            CriterionName = "Glucose máu",
            MeasuredValue = "85",
            Unit = "mg/dL",
            ReferenceInterval = "70 - 99 mg/dL",
            Flag = ResultFlag.Normal
        }
    }
};
await testResultsCol.InsertManyAsync(new[] { approvedResult, pendingResult });

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

var labToken = CreateJwt("USR-LAB-01", RoleNames.Lab, "Kỹ thuật viên Lab");
var managerToken = CreateJwt("USR-MGR-01", RoleNames.Manager, "Trưởng Lab");
var patient1Token = CreateJwt("USR-PATIENT-01", RoleNames.Patient, "Nguyen Van A");
var patient2Token = CreateJwt("USR-PATIENT-02", RoleNames.Patient, "Tran Thi B");
var recepToken = CreateJwt("USR-REC-01", RoleNames.Receptionist, "Le Receptionist");

string? createdNotifId = null;

try
{
    // =========================================================================
    // Check 1: SEC-NOTIF-01 - Kết quả đã duyệt -> gửi thông báo thành công
    // =========================================================================
    Console.WriteLine("\n[CHECK 1] SEC-NOTIF-01: Kết quả đã duyệt -> gửi thông báo thành công (200 OK)");
    SetAuth(http, labToken);

    var validDispatchDto = new DispatchTestResultNotificationDto
    {
        TestOrderId = "ORD-APP-01",
        PatientId = "PAT-2026-0001",
        PatientName = "Nguyen Van A",
        PatientEmail = "patient1@example.com",
        Channel = TestResultNotificationChannel.Email,
        TestPackageName = "Gói Xét Nghiệm Sinh Hóa Chuẩn"
    };

    var res1 = await http.PostAsJsonAsync("/api/notifications/test-results/dispatch", validDispatchDto);
    var body1 = await res1.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res1.StatusCode}, Body: {body1}");

    if (res1.StatusCode == HttpStatusCode.OK)
    {
        using var doc1 = JsonDocument.Parse(body1);
        var data1 = doc1.RootElement.GetProperty("data");
        createdNotifId = data1.GetProperty("id").GetString();
        var status = data1.GetProperty("status").GetString();
        var orderId = data1.GetProperty("testOrderId").GetString();

        if (!string.IsNullOrWhiteSpace(createdNotifId) && orderId == "ORD-APP-01" && status == "Delivered")
        {
            Console.WriteLine($" PASS: Notification dispatched successfully for approved order {orderId} (ID: {createdNotifId}, Status: {status})");
            passed++;
        }
        else
        {
            Console.WriteLine(" FAIL: Response data missing required fields.");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 200 OK, got {res1.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 2: SEC-NOTIF-02 - Kết quả chưa duyệt -> TUYỆT ĐỐI KHÔNG GỬI
    // =========================================================================
    Console.WriteLine("\n[CHECK 2] SEC-NOTIF-02: Kết quả chưa duyệt -> TUYỆT ĐỐI KHÔNG GỬI (400 Bad Request)");
    SetAuth(http, labToken);

    var pendingDispatchDto = new DispatchTestResultNotificationDto
    {
        TestOrderId = "ORD-PEND-02", // Pending / TechnicianVerified, NOT DoctorApproved
        PatientId = "PAT-2026-0001",
        PatientName = "Nguyen Van A",
        PatientEmail = "patient1@example.com",
        Channel = TestResultNotificationChannel.Email
    };

    var res2 = await http.PostAsJsonAsync("/api/notifications/test-results/dispatch", pendingDispatchDto);
    var body2 = await res2.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res2.StatusCode}, Body: {body2}");

    if (res2.StatusCode == HttpStatusCode.BadRequest && body2.Contains("chưa được phê duyệt"))
    {
        Console.WriteLine(" PASS: Unapproved result notification dispatch rejected with 400 Bad Request.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 400 Bad Request, got {res2.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 3: SEC-NOTIF-03 - Data Protection: Không lộ toàn bộ kết quả xét nghiệm trong email
    // =========================================================================
    Console.WriteLine("\n[CHECK 3] SEC-NOTIF-03: Data Protection: Nội dung thông báo không để lộ kết quả chi tiết trong email");
    var lastEmail = testEmailService.LastSentEmail;
    if (lastEmail != null)
    {
        bool hasConfidentialInvitation = lastEmail.Message.Contains("Kết quả xét nghiệm của bạn đã sẵn sàng. Vui lòng đăng nhập BMS để xem kết quả.");
        bool doesNotLeakRawResults = !lastEmail.Message.Contains("MeasuredValue") && !lastEmail.Message.Contains("85 mg/dL");

        if (hasConfidentialInvitation && doesNotLeakRawResults)
        {
            Console.WriteLine($" PASS: Content strictly adheres to data protection: '{lastEmail.Message}' (No raw PHI exposed).");
            passed++;
        }
        else
        {
            Console.WriteLine($" FAIL: Sensitive data exposed in email message: '{lastEmail.Message}'");
            failed++;
        }
    }
    else
    {
        Console.WriteLine(" FAIL: No email was captured.");
        failed++;
    }

    // =========================================================================
    // Check 4: SEC-NOTIF-04 - Gửi nhầm người nhận -> chặn bởi kiểm tra liên kết tài khoản
    // =========================================================================
    Console.WriteLine("\n[CHECK 4] SEC-NOTIF-04: Recipient Binding: Gửi nhầm email không liên kết -> Bị chặn");
    SetAuth(http, labToken);

    var wrongRecipientDto = new DispatchTestResultNotificationDto
    {
        TestOrderId = "ORD-APP-01",
        PatientId = "PAT-2026-0001",
        PatientName = "Nguyen Van A",
        PatientEmail = "hacker_recipient@evil.com", // Wrong email not belonging to PAT-2026-0001
        Channel = TestResultNotificationChannel.Email
    };

    var res4 = await http.PostAsJsonAsync("/api/notifications/test-results/dispatch", wrongRecipientDto);
    var body4 = await res4.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res4.StatusCode}, Body: {body4}");

    if (res4.StatusCode == HttpStatusCode.BadRequest && body4.Contains("không liên kết với hồ sơ bệnh nhân"))
    {
        Console.WriteLine(" PASS: Mismatched recipient email blocked by account binding check.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 400 Bad Request with recipient binding failure, got {res4.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 5: SEC-NOTIF-05 - Gửi thông báo thất bại -> Lưu trạng thái Failed để xử lý lại
    // =========================================================================
    Console.WriteLine("\n[CHECK 5] SEC-NOTIF-05: Gửi thông báo thất bại -> Lưu trạng thái Failed để xử lý lại");
    SetAuth(http, labToken);
    testEmailService.ShouldFail = true; // Simulate SMTP delivery failure

    var failDispatchDto = new DispatchTestResultNotificationDto
    {
        TestOrderId = "ORD-APP-01",
        PatientId = "PAT-2026-0001",
        PatientName = "Nguyen Van A",
        PatientEmail = "patient1@example.com",
        Channel = TestResultNotificationChannel.Email
    };

    var res5 = await http.PostAsJsonAsync("/api/notifications/test-results/dispatch", failDispatchDto);
    var body5 = await res5.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res5.StatusCode}, Body: {body5}");

    var notifsCol = db.GetCollection<TestResultNotification>("test_result_notifications");
    var failedNotif = await notifsCol.Find(x => x.Status == NotificationDeliveryStatus.Failed).FirstOrDefaultAsync();

    if (res5.StatusCode == HttpStatusCode.BadRequest && failedNotif != null && failedNotif.Status == NotificationDeliveryStatus.Failed)
    {
        Console.WriteLine($" PASS: Failed notification recorded in database with Status=Failed, FailureReason: '{failedNotif.FailureReason}' (ID: {failedNotif.Id})");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected Failed notification record. Got status {res5.StatusCode}, DB record: {failedNotif?.Status}");
        failed++;
    }

    // =========================================================================
    // Check 6: SEC-NOTIF-06 - Xử lý lại (Resend / Retry) thông báo thất bại
    // =========================================================================
    Console.WriteLine("\n[CHECK 6] SEC-NOTIF-06: Xử lý lại (Resend / Retry) thông báo thất bại -> 200 OK");
    testEmailService.ShouldFail = false; // Restore email service

    if (failedNotif != null)
    {
        var resendDto = new ResendNotificationDto();
        var res6 = await http.PostAsJsonAsync($"/api/notifications/test-results/{failedNotif.Id}/resend", resendDto);
        var body6 = await res6.Content.ReadAsStringAsync();
        Console.WriteLine($"Status: {res6.StatusCode}, Body: {body6}");

        var retriedNotif = await notifsCol.Find(x => x.Id == failedNotif.Id).FirstOrDefaultAsync();

        if (res6.StatusCode == HttpStatusCode.OK && retriedNotif != null && retriedNotif.Status == NotificationDeliveryStatus.Delivered && retriedNotif.RetryCount == 1)
        {
            Console.WriteLine($" PASS: Successfully retried notification (RetryCount={retriedNotif.RetryCount}, Status={retriedNotif.Status}).");
            passed++;
        }
        else
        {
            Console.WriteLine($" FAIL: Expected 200 OK and RetryCount=1. Got {res6.StatusCode}, RetryCount={retriedNotif?.RetryCount}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine(" FAIL: Cannot test resend because failedNotif was null.");
        failed++;
    }

    // =========================================================================
    // Check 7: SEC-NOTIF-07 - ISO 27789 Audit Trail for notification events
    // =========================================================================
    Console.WriteLine("\n[CHECK 7] SEC-NOTIF-07: ISO 27789: Audit Trail generated in audit_logs");
    var auditCol = db.GetCollection<AuditLog>("audit_logs");
    var auditLogs = await auditCol.Find(x => x.Action == "NOTIFY_TEST_RESULT" || x.Action == "RESEND_TEST_RESULT_NOTIFICATION").ToListAsync();

    if (auditLogs.Count >= 2)
    {
        Console.WriteLine($" PASS: Found {auditLogs.Count} audit records for notification operations in audit_logs.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected at least 2 audit logs, found {auditLogs.Count}");
        failed++;
    }

    // =========================================================================
    // Check 8: SEC-NOTIF-08 - OWASP ASVS IDOR: Patient B accesses Patient A's notification -> 403 Forbidden
    // =========================================================================
    Console.WriteLine("\n[CHECK 8] SEC-NOTIF-08: OWASP ASVS IDOR: Patient B accesses Patient A's notification -> 403 Forbidden");
    SetAuth(http, patient2Token); // Patient B (Tran Thi B)

    var res8 = await http.GetAsync($"/api/notifications/test-results/{createdNotifId}");
    var body8 = await res8.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res8.StatusCode}, Body: {body8}");

    if (res8.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine(" PASS: Cross-patient notification access blocked with 403 Forbidden.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 403 Forbidden, got {res8.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 9: SEC-NOTIF-09 - OWASP ASVS IDOR: Patient B views Patient A's list -> 403 Forbidden
    // =========================================================================
    Console.WriteLine("\n[CHECK 9] SEC-NOTIF-09: OWASP ASVS IDOR: Patient B accesses Patient A's notifications list -> 403 Forbidden");
    SetAuth(http, patient2Token); // Patient B accessing PAT-2026-0001 list

    var res9 = await http.GetAsync("/api/notifications/test-results/patient/PAT-2026-0001");
    Console.WriteLine($"Status: {res9.StatusCode}");

    if (res9.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine(" PASS: Patient list endpoint blocked with 403 Forbidden.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 403 Forbidden, got {res9.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 10: SEC-NOTIF-10 - Patient A views their own notification -> 200 OK
    // =========================================================================
    Console.WriteLine("\n[CHECK 10] SEC-NOTIF-10: Patient A accesses their own notification -> 200 OK");
    SetAuth(http, patient1Token); // Patient A

    var res10 = await http.GetAsync($"/api/notifications/test-results/{createdNotifId}");
    var body10 = await res10.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res10.StatusCode}");

    if (res10.StatusCode == HttpStatusCode.OK)
    {
        using var doc10 = JsonDocument.Parse(body10);
        var pId = doc10.RootElement.GetProperty("data").GetProperty("patientId").GetString();
        if (pId == "PAT-2026-0001")
        {
            Console.WriteLine($" PASS: Patient A successfully accessed their own notification (PatientId: {pId}).");
            passed++;
        }
        else
        {
            Console.WriteLine($" FAIL: PatientId mismatch: {pId}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 200 OK, got {res10.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 11: SEC-NOTIF-11 - Unauthenticated request -> 401 Unauthorized
    // =========================================================================
    Console.WriteLine("\n[CHECK 11] SEC-NOTIF-11: Unauthenticated request -> 401 Unauthorized");
    SetAuth(http, null);

    var res11 = await http.GetAsync($"/api/notifications/test-results/{createdNotifId}");
    Console.WriteLine($"Status: {res11.StatusCode}");

    if (res11.StatusCode == HttpStatusCode.Unauthorized)
    {
        Console.WriteLine(" PASS: Unauthenticated access rejected with 401 Unauthorized.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 401 Unauthorized, got {res11.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 12: SEC-NOTIF-12 - RBAC: Unauthorized roles cannot dispatch notifications
    // =========================================================================
    Console.WriteLine("\n[CHECK 12] SEC-NOTIF-12: RBAC: Unauthorized role (Patient / Receptionist) cannot dispatch -> 403 Forbidden");
    SetAuth(http, patient1Token);

    var res12 = await http.PostAsJsonAsync("/api/notifications/test-results/dispatch", validDispatchDto);
    Console.WriteLine($"Status: {res12.StatusCode}");

    if (res12.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine(" PASS: Non-staff role blocked from dispatching notifications with 403 Forbidden.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 403 Forbidden, got {res12.StatusCode}");
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
Console.WriteLine($"UC60 Verification Summary: {passed} PASSED, {failed} FAILED (Total: {passed + failed})");
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

public class TestEmailService : IEmailService
{
    public bool ShouldFail { get; set; } = false;

    public record SentEmail(string ToEmail, string PatientName, string TestOrderId, string Title, string Message, bool IsPanic);
    public SentEmail? LastSentEmail { get; private set; }

    public Task<bool> SendOtpEmailAsync(string toEmail, string otpCode, string purposeDescription, int expiryMinutes = 5, CancellationToken ct = default)
        => Task.FromResult(!ShouldFail);

    public Task<bool> SendAppointmentConfirmationEmailAsync(string toEmail, string patientName, string appointmentCode, string packageName, string appointmentDateTime, string location, string fastingNotes, CancellationToken ct = default)
        => Task.FromResult(!ShouldFail);

    public Task<bool> SendTestResultNotificationEmailAsync(string toEmail, string patientName, string testOrderId, string? testPackageName, string title, string message, bool isCriticalPanicValue, List<string>? criticalParameters, string? clinicalRecommendation, CancellationToken ct = default)
    {
        if (ShouldFail) return Task.FromResult(false);
        LastSentEmail = new SentEmail(toEmail, patientName, testOrderId, title, message, isCriticalPanicValue);
        return Task.FromResult(true);
    }
}
