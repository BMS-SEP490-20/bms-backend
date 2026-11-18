using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BMS.Api.Authorization;
using BMS.Api.Controllers.Clinic;
using BMS.Application.DTOs.Clinic;
using BMS.Application.DTOs.MedicalRecords;
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
Console.WriteLine("UC50 — Add Medical Assessment: Clinical Documentation, Anti-Spoofing & Traceability Checks");
Console.WriteLine("Standards: ISO 15189:2022 | ISO 27789:2021 | OWASP ASVS");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc50_test_" + Guid.NewGuid().ToString("N")[..8];
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

builder.Services.AddScoped<IMedicalAssessmentRepository, MedicalAssessmentRepository>();
builder.Services.AddScoped<IPatientRepository, PatientRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();
builder.Services.AddScoped<IPatientOwnership, PatientOwnershipRepository>();
builder.Services.AddScoped<IMedicalAssessmentService, MedicalAssessmentService>();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(MedicalAssessmentsController).Assembly);

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
    Id = "PAT-2026-00001",
    PatientCode = "PAT-2026-00001",
    FullName = "Nguyen Van A",
    DateOfBirth = new DateTime(1990, 5, 12, 0, 0, 0, DateTimeKind.Utc),
    Gender = BiologicalGender.Male,
    PhoneNumber = "0901234567",
    Email = "patient1@example.com",
    Address = "123 Le Loi, Q1, HCMC",
    CreatedAt = DateTime.UtcNow
};
var p2 = new Patient
{
    Id = "PAT-2026-00002",
    PatientCode = "PAT-2026-00002",
    FullName = "Tran Thi B",
    DateOfBirth = new DateTime(1995, 8, 20, 0, 0, 0, DateTimeKind.Utc),
    Gender = BiologicalGender.Female,
    PhoneNumber = "0909876543",
    Email = "patient2@example.com",
    Address = "456 Tran Hung Dao, Q5, HCMC",
    CreatedAt = DateTime.UtcNow
};
await patientsCol.InsertManyAsync(new[] { p1, p2 });

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

var doctorToken = CreateJwt("USR-DOCTOR-01", RoleNames.Doctor, "BS. Nguyen Van Doc");
var patient1Token = CreateJwt("PAT-2026-00001", RoleNames.Patient, "Nguyen Van A");
var patient2Token = CreateJwt("PAT-2026-00002", RoleNames.Patient, "Tran Thi B");
var recepToken = CreateJwt("USR-RECEP-01", RoleNames.Receptionist, "Le Receptionist");
var labToken = CreateJwt("USR-LAB-01", RoleNames.Lab, "Pham LabTech");

string? createdAssessmentId = null;

try
{
    // =========================================================================
    // Check 1: SEC-ASSESS-01 - Doctor creates valid Medical Assessment -> 201 Created
    // =========================================================================
    Console.WriteLine("\n[CHECK 1] SEC-ASSESS-01: Doctor creates valid Medical Assessment -> 201 Created");
    SetAuth(http, doctorToken);

    var createDto = new CreateMedicalAssessmentDto
    {
        PatientId = "PAT-2026-00001",
        OrderId = "ORD-2026-1001",
        ChiefComplaint = "Sốt cao 3 ngày kèm ho khan, đau họng",
        ClinicalSymptoms = "Sốt 38.5 độ C, amidan sưng đỏ, phổi trong",
        PhysicalExamination = "Tim đều, phổi không rale, họng đỏ xung huyết",
        Icd10Code = "J00",
        Icd10Name = "Viêm mũi họng cấp tính [cảm thường]",
        PreliminaryDiagnosis = "Viêm họng cấp",
        FinalDiagnosis = "Viêm mũi họng cấp tính do virus",
        Severity = "Moderate",
        RiskLevel = "Low",
        Vitals = new VitalSignsInputDto
        {
            HeartRate = 82,
            SystolicBp = 120,
            DiastolicBp = 80,
            Temperature = 38.5m,
            Spo2 = 98,
            HeightCm = 170m,
            WeightKg = 65m
        },
        TreatmentPlan = "Uống nhiều nước, hạ sốt bằng Paracetamol khi sốt >= 38.5 độ C",
        PrescriptionNotes = "Paracetamol 500mg x 3 lần/ngày khi cần",
        FollowUpNotes = "Tái khám sau 3 ngày nếu không giảm sốt"
    };

    var res1 = await http.PostAsJsonAsync("/api/medical-assessments", createDto);
    var body1 = await res1.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res1.StatusCode}, Body: {body1}");

    if (res1.StatusCode == HttpStatusCode.Created)
    {
        using var doc1 = JsonDocument.Parse(body1);
        var root = doc1.RootElement;
        var data = root.GetProperty("data");
        createdAssessmentId = data.GetProperty("id").GetString();
        var code = data.GetProperty("assessmentCode").GetString();
        var doctorId = data.GetProperty("doctorId").GetString();

        if (!string.IsNullOrWhiteSpace(createdAssessmentId) && !string.IsNullOrWhiteSpace(code) && doctorId == "USR-DOCTOR-01")
        {
            Console.WriteLine($" PASS: Created assessment {code} (ID: {createdAssessmentId}) by Doctor {doctorId}");
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
        Console.WriteLine($" FAIL: Expected 201 Created, got {res1.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 2: SEC-ASSESS-02 - Doctor Anti-Spoofing: CreatedBy and DoctorId derived from token
    // =========================================================================
    Console.WriteLine("\n[CHECK 2] SEC-ASSESS-02: Identity Anti-Spoofing: DoctorId & CreatedBy bound to authenticated token");
    SetAuth(http, doctorToken);

    var assessmentsCol = db.GetCollection<MedicalAssessment>("medical_assessments");
    var entity = await assessmentsCol.Find(x => x.Id == createdAssessmentId).FirstOrDefaultAsync();

    if (entity != null && entity.DoctorId == "USR-DOCTOR-01" && entity.CreatedBy == "USR-DOCTOR-01")
    {
        Console.WriteLine($" PASS: DoctorId ({entity.DoctorId}) and CreatedBy ({entity.CreatedBy}) strictly match JWT token identity.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: DoctorId or CreatedBy does not match token identity: DoctorId={entity?.DoctorId}, CreatedBy={entity?.CreatedBy}");
        failed++;
    }

    // =========================================================================
    // Check 3: SEC-ASSESS-03 - Order Association: Linked to Patient ID and Order ID
    // =========================================================================
    Console.WriteLine("\n[CHECK 3] SEC-ASSESS-03: Diagnostic Order Association: Linked to Patient ID and Order ID");
    if (entity != null && entity.PatientId == "PAT-2026-00001" && entity.OrderId == "ORD-2026-1001")
    {
        Console.WriteLine($" PASS: Linked to Patient ID '{entity.PatientId}' and Order ID '{entity.OrderId}'.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Patient ID ({entity?.PatientId}) or Order ID ({entity?.OrderId}) mismatch.");
        failed++;
    }

    // =========================================================================
    // Check 4: SEC-ASSESS-04 - Patient role cannot create assessment -> 403 Forbidden
    // =========================================================================
    Console.WriteLine("\n[CHECK 4] SEC-ASSESS-04: RBAC: Patient role attempts to create Assessment -> 403 Forbidden");
    SetAuth(http, patient1Token);

    var res4 = await http.PostAsJsonAsync("/api/medical-assessments", createDto);
    Console.WriteLine($"Status: {res4.StatusCode}");
    if (res4.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine(" PASS: Patient blocked from creating medical assessment with 403 Forbidden.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 403 Forbidden, got {res4.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 5: SEC-ASSESS-05 - Receptionist role cannot create assessment -> 403 Forbidden
    // =========================================================================
    Console.WriteLine("\n[CHECK 5] SEC-ASSESS-05: RBAC: Receptionist role attempts to create Assessment -> 403 Forbidden");
    SetAuth(http, recepToken);

    var res5 = await http.PostAsJsonAsync("/api/medical-assessments", createDto);
    Console.WriteLine($"Status: {res5.StatusCode}");
    if (res5.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine(" PASS: Receptionist blocked from creating medical assessment with 403 Forbidden.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 403 Forbidden, got {res5.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 6: SEC-ASSESS-06 - LabTechnician role cannot create assessment -> 403 Forbidden
    // =========================================================================
    Console.WriteLine("\n[CHECK 6] SEC-ASSESS-06: RBAC: LabTechnician role attempts to create Assessment -> 403 Forbidden");
    SetAuth(http, labToken);

    var res6 = await http.PostAsJsonAsync("/api/medical-assessments", createDto);
    Console.WriteLine($"Status: {res6.StatusCode}");
    if (res6.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine(" PASS: LabTechnician blocked from creating medical assessment with 403 Forbidden.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 403 Forbidden, got {res6.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 7: SEC-ASSESS-07 - Non-existent Patient ID -> 400 Bad Request (PATIENT_NOT_FOUND)
    // =========================================================================
    Console.WriteLine("\n[CHECK 7] SEC-ASSESS-07: Validation: Non-existent Patient ID -> 400 Bad Request");
    SetAuth(http, doctorToken);

    var invalidDto = new CreateMedicalAssessmentDto
    {
        PatientId = "PAT-NON-EXISTENT-999",
        ChiefComplaint = "Đau đầu nhẹ",
        Icd10Code = "R51",
        FinalDiagnosis = "Nhức đầu căng thẳng",
        Vitals = new VitalSignsInputDto { HeartRate = 75, SystolicBp = 120, DiastolicBp = 80, HeightCm = 170, WeightKg = 60 }
    };

    var res7 = await http.PostAsJsonAsync("/api/medical-assessments", invalidDto);
    var body7 = await res7.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res7.StatusCode}, Body: {body7}");

    if (res7.StatusCode == HttpStatusCode.BadRequest && body7.Contains("PATIENT_NOT_FOUND"))
    {
        Console.WriteLine(" PASS: Rejected with 400 Bad Request and PATIENT_NOT_FOUND error.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 400 with PATIENT_NOT_FOUND, got {res7.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 8: SEC-ASSESS-08 - Scope Distinction: IsAutomatedDiagnosis == false, ClinicalConsultation
    // =========================================================================
    Console.WriteLine("\n[CHECK 8] SEC-ASSESS-08: Scope: Distinct from automated diagnosis (ClinicalConsultation)");
    if (entity != null && !entity.IsAutomatedDiagnosis && entity.AssessmentType == "ClinicalConsultation")
    {
        Console.WriteLine($" PASS: IsAutomatedDiagnosis={entity.IsAutomatedDiagnosis}, AssessmentType='{entity.AssessmentType}' (Clinical physician consultation verified).");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Scope mismatch: IsAutomatedDiagnosis={entity?.IsAutomatedDiagnosis}, AssessmentType={entity?.AssessmentType}");
        failed++;
    }

    // =========================================================================
    // Check 9: SEC-ASSESS-09 - Doctor updates assessment -> 200 OK & Version incremented
    // =========================================================================
    Console.WriteLine("\n[CHECK 9] SEC-ASSESS-09: Versioning: Doctor updates draft Assessment -> 200 OK, Version=2");
    SetAuth(http, doctorToken);

    var updateDto = new UpdateMedicalAssessmentDto
    {
        ChiefComplaint = "Sốt cao 3 ngày kèm ho khan, đau rát họng tăng",
        ClinicalSymptoms = "Sốt 38.8 độ C, amidan sưng to, sung huyết rõ",
        PhysicalExamination = "Họng sưng đỏ, không giả mạc, hạch cổ góc hàm mềm sưng nhẹ",
        Icd10Code = "J00",
        Icd10Name = "Viêm mũi họng cấp tính [cảm thường]",
        PreliminaryDiagnosis = "Viêm họng cấp",
        FinalDiagnosis = "Viêm amidan họng cấp tính do virus",
        Severity = "Moderate",
        RiskLevel = "Low",
        Vitals = new VitalSignsInputDto
        {
            HeartRate = 85,
            SystolicBp = 122,
            DiastolicBp = 82,
            Temperature = 38.8m,
            Spo2 = 98,
            HeightCm = 170m,
            WeightKg = 65m
        },
        TreatmentPlan = "Hạ sốt, bù nước điện giải Oresol, súc họng nước muối",
        FollowUpNotes = "Tái khám sau 2 ngày nếu còn sốt cao"
    };

    var res9 = await http.PutAsJsonAsync($"/api/medical-assessments/{createdAssessmentId}", updateDto);
    var body9 = await res9.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res9.StatusCode}, Body: {body9}");

    var updatedEntity = await assessmentsCol.Find(x => x.Id == createdAssessmentId).FirstOrDefaultAsync();

    if (res9.StatusCode == HttpStatusCode.OK && updatedEntity != null && updatedEntity.Version == 2 && updatedEntity.UpdatedAt != null && updatedEntity.UpdatedBy == "USR-DOCTOR-01")
    {
        Console.WriteLine($" PASS: Updated successfully. Version={updatedEntity.Version}, UpdatedAt={updatedEntity.UpdatedAt:u}, UpdatedBy={updatedEntity.UpdatedBy}");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 200 OK and Version=2. Got status {res9.StatusCode}, Version={updatedEntity?.Version}");
        failed++;
    }

    // =========================================================================
    // Check 10: SEC-ASSESS-10 - ISO 27789 Audit Trail: Update logged to audit_logs
    // =========================================================================
    Console.WriteLine("\n[CHECK 10] SEC-ASSESS-10: ISO 27789: Audit Trail generated for Assessment modification");
    var auditCol = db.GetCollection<AuditLog>("audit_logs");
    var auditEntries = await auditCol.Find(x => x.EntityId == createdAssessmentId).ToListAsync();

    var hasCreateAudit = auditEntries.Any(x => x.Action == "CREATE_MEDICAL_ASSESSMENT" && x.UserId == "USR-DOCTOR-01");
    var hasUpdateAudit = auditEntries.Any(x => x.Action == "UPDATE_MEDICAL_ASSESSMENT" && x.UserId == "USR-DOCTOR-01");

    if (hasCreateAudit && hasUpdateAudit)
    {
        Console.WriteLine($" PASS: ISO 27789 Audit entries verified. Found {auditEntries.Count} logs (CREATE_MEDICAL_ASSESSMENT and UPDATE_MEDICAL_ASSESSMENT).");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Audit log missing. Total entries={auditEntries.Count}, hasCreate={hasCreateAudit}, hasUpdate={hasUpdateAudit}");
        failed++;
    }

    // =========================================================================
    // Check 11: SEC-ASSESS-11 - OWASP ASVS IDOR Protection: Patient B cannot view Patient A's assessment
    // =========================================================================
    Console.WriteLine("\n[CHECK 11] SEC-ASSESS-11: OWASP ASVS IDOR: Patient B accesses Patient A's assessment -> 403 Forbidden");
    SetAuth(http, patient2Token); // Patient 2 trying to read Patient 1's assessment

    var res11 = await http.GetAsync($"/api/medical-assessments/{createdAssessmentId}");
    var body11 = await res11.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res11.StatusCode}, Body: {body11}");

    if (res11.StatusCode == HttpStatusCode.Forbidden)
    {
        Console.WriteLine(" PASS: IDOR attempt thwarted with 403 Forbidden.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 403 Forbidden for cross-patient access, got {res11.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 12: SEC-ASSESS-12 - Patient A views their own assessment -> 200 OK
    // =========================================================================
    Console.WriteLine("\n[CHECK 12] SEC-ASSESS-12: Patient A accesses their own assessment -> 200 OK");
    SetAuth(http, patient1Token); // Patient 1 viewing their own assessment

    var res12 = await http.GetAsync($"/api/medical-assessments/{createdAssessmentId}");
    var body12 = await res12.Content.ReadAsStringAsync();
    Console.WriteLine($"Status: {res12.StatusCode}");

    if (res12.StatusCode == HttpStatusCode.OK)
    {
        using var doc12 = JsonDocument.Parse(body12);
        var pId = doc12.RootElement.GetProperty("data").GetProperty("patientId").GetString();
        if (pId == "PAT-2026-00001")
        {
            Console.WriteLine($" PASS: Patient 1 accessed own assessment successfully (PatientId: {pId}).");
            passed++;
        }
        else
        {
            Console.WriteLine($" FAIL: PatientId mismatch in returned data: {pId}");
            failed++;
        }
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 200 OK for own assessment access, got {res12.StatusCode}");
        failed++;
    }

    // =========================================================================
    // Check 13: SEC-ASSESS-13 - Unauthenticated access -> 401 Unauthorized
    // =========================================================================
    Console.WriteLine("\n[CHECK 13] SEC-ASSESS-13: Unauthenticated request -> 401 Unauthorized");
    SetAuth(http, null);

    var res13 = await http.GetAsync($"/api/medical-assessments/{createdAssessmentId}");
    Console.WriteLine($"Status: {res13.StatusCode}");

    if (res13.StatusCode == HttpStatusCode.Unauthorized)
    {
        Console.WriteLine(" PASS: Unauthenticated access rejected with 401 Unauthorized.");
        passed++;
    }
    else
    {
        Console.WriteLine($" FAIL: Expected 401 Unauthorized, got {res13.StatusCode}");
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
Console.WriteLine($"UC50 Verification Summary: {passed} PASSED, {failed} FAILED (Total: {passed + failed})");
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
