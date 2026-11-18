using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BMS.Api.Authorization;
using BMS.Api.Controllers.Users;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Shifts;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Constants;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
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
Console.WriteLine("UC35 — Staff Shift Management (UC35.1 to UC35.5): Automated Security & Compliance Checks");
Console.WriteLine("Standards: OWASP ASVS (Access Control & Business Logic) | ISO 27789 | ISO/IEC 25010");
Console.WriteLine("================================================================================");

var passed = 0;
var failed = 0;

var mongoUri = Environment.GetEnvironmentVariable("MONGODB_URI") ?? "mongodb://127.0.0.1:27017";
var testDbName = "bms_uc35_test_" + Guid.NewGuid().ToString("N")[..8];
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

builder.Services.AddSingleton<IShiftRepository, ShiftRepository>();
builder.Services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IShiftService, ShiftService>();

builder.Services.AddControllers()
    .AddApplicationPart(typeof(ShiftsController).Assembly);

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

var docAId = ObjectId.GenerateNewId().ToString();
var docBId = ObjectId.GenerateNewId().ToString();

var managerToken = GenerateToken("USR-MANAGER-01", RoleNames.Manager, "Quan Ly Phong Kham");
var doctorAToken = GenerateToken(docAId, RoleNames.Doctor, "Bac Si A");
var doctorBToken = GenerateToken(docBId, RoleNames.Doctor, "Bac Si B");
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
    // SEED DATABASE: Accounts, Doctors, Rooms, Shifts
    // =========================================================================
    var accountsCol = db.GetCollection<User>("accounts");
    var doctorsCol = db.GetCollection<Doctor>("doctors");
    var roomsCol = db.GetCollection<DoctorRoom>("doctor_rooms");
    var shiftsCol = db.GetCollection<DoctorShift>("doctor_shifts");
    var auditLogsCol = db.GetCollection<AuditLog>("audit_logs");

    var doctorAAccount = new User
    {
        Id = docAId,
        Username = "doctor_a",
        FullName = "Bac Si Nguyen Van A",
        Email = "doctor_a@bms.vn",
        RoleId = (int)UserRole.Doctor,
        Status = (int)UserStatus.Active
    };

    var doctorBAccount = new User
    {
        Id = docBId,
        Username = "doctor_b",
        FullName = "Bac Si Le Van B",
        Email = "doctor_b@bms.vn",
        RoleId = (int)UserRole.Doctor,
        Status = (int)UserStatus.Active
    };

    var inactiveStaffId = ObjectId.GenerateNewId().ToString();
    var inactiveStaff = new User
    {
        Id = inactiveStaffId,
        Username = "staff_inactive",
        FullName = "Nhan Vien Khoa",
        Email = "inactive@bms.vn",
        RoleId = (int)UserRole.Doctor,
        Status = (int)UserStatus.Inactive
    };

    await accountsCol.InsertManyAsync([doctorAAccount, doctorBAccount, inactiveStaff]);

    var doctorADoc = new Doctor
    {
        Id = docAId,
        FullName = "Bac Si Nguyen Van A",
        Specialty = "Noi khoa"
    };
    var doctorBDoc = new Doctor
    {
        Id = docBId,
        FullName = "Bac Si Le Van B",
        Specialty = "Nhi khoa"
    };
    await doctorsCol.InsertManyAsync([doctorADoc, doctorBDoc]);

    var room101 = new DoctorRoom
    {
        Id = "ROOM-101",
        RoomName = "Phong Kham Noi 1",
        RoomNumber = "ROOM-101",
        Specialty = "Noi khoa",
        Floor = "1"
    };
    var room102 = new DoctorRoom
    {
        Id = "ROOM-102",
        RoomName = "Phong Kham Nhi 1",
        RoomNumber = "ROOM-102",
        Specialty = "Nhi khoa",
        Floor = "1"
    };
    var roomGen = new DoctorRoom
    {
        Id = "ROOM-GEN",
        RoomName = "Phong Kham Tong Quat",
        RoomNumber = "ROOM-GEN",
        Specialty = "Tổng quát",
        Floor = "1"
    };
    await roomsCol.InsertManyAsync([room101, room102, roomGen]);

    // Seed Shifts for Doctor A and Doctor B tomorrow
    var tomorrow = fixedNow.UtcDateTime.Date.AddDays(1);

    var shiftA1 = new DoctorShift
    {
        Id = "SHIFT-DOC-A-01",
        DoctorId = docAId,
        EmployeeName = "Bac Si Nguyen Van A",
        EmployeeRole = "Doctor",
        Department = "Noi khoa",
        RoomId = "ROOM-101",
        RoomName = "Phong Kham Noi 1",
        ShiftDate = tomorrow,
        ShiftType = "Morning",
        ShiftStart = "07:30",
        ShiftEnd = "11:30",
        MaxPatients = 30,
        CurrentPatientsCount = 0,
        Status = "Approved",
        CreatedAt = fixedNow.UtcDateTime,
        CreatedBy = "USR-MANAGER-01"
    };

    var shiftB1 = new DoctorShift
    {
        Id = "SHIFT-DOC-B-01",
        DoctorId = docBId,
        EmployeeName = "Bac Si Le Van B",
        EmployeeRole = "Doctor",
        Department = "Nhi khoa",
        RoomId = "ROOM-102",
        ShiftDate = tomorrow,
        ShiftType = "Morning",
        ShiftStart = "07:30",
        ShiftEnd = "11:30",
        MaxPatients = 25,
        CurrentPatientsCount = 0,
        Status = "Approved",
        CreatedAt = fixedNow.UtcDateTime,
        CreatedBy = "USR-MANAGER-01"
    };

    // Shift completed/locked for testing update locked rule
    var shiftLocked = new DoctorShift
    {
        Id = "SHIFT-LOCKED-01",
        DoctorId = docAId,
        EmployeeName = "Bac Si Nguyen Van A",
        EmployeeRole = "Doctor",
        Department = "Noi khoa",
        RoomId = "ROOM-101",
        ShiftDate = tomorrow.AddDays(2),
        ShiftType = "Morning",
        ShiftStart = "07:30",
        ShiftEnd = "11:30",
        MaxPatients = 30,
        CurrentPatientsCount = 0,
        Status = "Completed",
        CreatedAt = fixedNow.UtcDateTime,
        CreatedBy = "USR-MANAGER-01"
    };

    await shiftsCol.InsertManyAsync([shiftA1, shiftB1, shiftLocked]);

    // =========================================================================
    // UC35.1: VIEW SHIFT LIST
    // =========================================================================

    await RunCheck("SEC-SHIFT-01 [UC35.1]: Manager xem danh sách toàn bộ ca trực trong hệ thống -> 200 OK & trả về đủ ca", async () =>
    {
        SetAuth(http, managerToken);
        var res = await http.GetAsync("/api/shifts");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");

        var paged = await res.Content.ReadFromJsonAsync<PagedResult<ShiftDto>>();
        Assert(paged != null, "PagedResult should not be null");
        Assert(paged!.TotalItems >= 3, $"Manager should see all shifts (>=3), got {paged.TotalItems}");
    });

    await RunCheck("SEC-SHIFT-02 [UC35.1]: Nhân viên (Doctor A) xem danh sách ca -> Bị giới hạn chỉ thấy ca của bản thân", async () =>
    {
        SetAuth(http, doctorAToken);
        var res = await http.GetAsync("/api/shifts");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");

        var paged = await res.Content.ReadFromJsonAsync<PagedResult<ShiftDto>>();
        Assert(paged != null, "PagedResult should not be null");
        // All returned items must strictly belong to USR-DOCTOR-01
        Assert(paged!.Items.All(s => s.EmployeeId == docAId), "Doctor A must only see their own shifts");
        Assert(paged.Items.Any(s => s.Id == "SHIFT-DOC-A-01"), "Must contain Doctor A's shift");
        Assert(!paged.Items.Any(s => s.Id == "SHIFT-DOC-B-01"), "Must NOT contain Doctor B's shift");
    });

    await RunCheck("SEC-SHIFT-03 [UC35.1]: Lọc danh sách ca theo ngày (FromDate, ToDate) -> Trả về kết quả chính xác", async () =>
    {
        SetAuth(http, managerToken);
        var dateStr = tomorrow.ToString("yyyy-MM-dd");
        var res = await http.GetAsync($"/api/shifts?fromDate={dateStr}&toDate={dateStr}");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");

        var paged = await res.Content.ReadFromJsonAsync<PagedResult<ShiftDto>>();
        Assert(paged != null, "PagedResult should not be null");
        Assert(paged!.Items.All(s => s.ShiftDate.Date == tomorrow.Date), "All filtered shifts must match tomorrow's date");
    });

    await RunCheck("SEC-SHIFT-04 [UC35.1]: Patient gọi API xem danh sách ca trực -> 403 Forbidden", async () =>
    {
        SetAuth(http, patientToken);
        var res = await http.GetAsync("/api/shifts");
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden, got {res.StatusCode}");
    });

    // =========================================================================
    // UC35.2: CREATE SHIFT
    // =========================================================================

    string createdShiftId = "";

    await RunCheck("SEC-SHIFT-05 [UC35.2]: Manager tạo ca trực hợp lệ -> 201 Created & Ghi nhận Audit Trail", async () =>
    {
        SetAuth(http, managerToken);
        var req = new CreateShiftRequest
        {
            EmployeeId = docAId,
            RoomId = "ROOM-101",
            ShiftDate = tomorrow.AddDays(5),
            ShiftType = "Afternoon",
            ShiftStart = "13:30",
            ShiftEnd = "17:00",
            MaxPatients = 20,
            Notes = "Ca chieu chuyen khoa Noi"
        };

        var res = await http.PostAsJsonAsync("/api/shifts", req);
        Assert(res.StatusCode == HttpStatusCode.Created, $"Expected 201 Created, got {res.StatusCode}");

        var dto = await res.Content.ReadFromJsonAsync<ShiftDto>();
        Assert(dto != null, "Response must contain ShiftDto");
        Assert(!string.IsNullOrWhiteSpace(dto!.Id), "Shift ID must be generated");
        Assert(dto.ShiftStart == "13:30" && dto.ShiftEnd == "17:00", "Working hours must match");
        createdShiftId = dto.Id;

        // Verify Audit Trail
        var audit = await auditLogsCol.Find(a => a.Action == "CREATE_SHIFT" && a.EntityId == createdShiftId).FirstOrDefaultAsync();
        Assert(audit != null, "Audit log for CREATE_SHIFT must exist");
        Assert(audit!.Status == "SUCCESS", $"Status must be SUCCESS, got {audit.Status}");
        Assert(audit.Severity == "INFO", $"Severity must be INFO, got {audit.Severity}");
    });

    await RunCheck("SEC-SHIFT-06 [UC35.2]: Giờ kết thúc trước hoặc bằng giờ bắt đầu -> 400 Bad Request (INVALID_WORKING_TIME)", async () =>
    {
        SetAuth(http, managerToken);
        var req = new CreateShiftRequest
        {
            EmployeeId = docAId,
            RoomId = "ROOM-101",
            ShiftDate = tomorrow.AddDays(6),
            ShiftType = "Morning",
            ShiftStart = "11:30",
            ShiftEnd = "07:30", // End before Start
            MaxPatients = 20
        };

        var res = await http.PostAsJsonAsync("/api/shifts", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request, got {res.StatusCode}");
    });

    await RunCheck("SEC-SHIFT-07 [UC35.2]: Nhân viên không tồn tại -> 400 Bad Request (STAFF_NOT_FOUND)", async () =>
    {
        SetAuth(http, managerToken);
        var req = new CreateShiftRequest
        {
            EmployeeId = "USR-NONEXISTENT-9999",
            RoomId = "ROOM-101",
            ShiftDate = tomorrow.AddDays(6),
            ShiftType = "Morning",
            ShiftStart = "07:30",
            ShiftEnd = "11:30",
            MaxPatients = 20
        };

        var res = await http.PostAsJsonAsync("/api/shifts", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request, got {res.StatusCode}");
    });

    await RunCheck("SEC-SHIFT-08 [UC35.2]: Tạo ca trùng/chồng chéo thời gian cho cùng nhân viên -> 409 Conflict (SHIFT_EMPLOYEE_CONFLICT)", async () =>
    {
        SetAuth(http, managerToken);
        // Doctor A already has shift SHIFT-DOC-A-01 on tomorrow (07:30 - 11:30). Overlap with 08:00 - 12:00
        var req = new CreateShiftRequest
        {
            EmployeeId = docAId,
            RoomId = "ROOM-GEN",
            ShiftDate = tomorrow,
            ShiftType = "Morning",
            ShiftStart = "08:00",
            ShiftEnd = "12:00",
            MaxPatients = 20
        };

        var res = await http.PostAsJsonAsync("/api/shifts", req);
        Assert(res.StatusCode == HttpStatusCode.Conflict, $"Expected 409 Conflict, got {res.StatusCode}");
    });

    await RunCheck("SEC-SHIFT-09 [UC35.2]: Nhân viên không có quyền (Doctor/Patient) gọi API tạo ca -> 403 Forbidden", async () =>
    {
        SetAuth(http, doctorAToken);
        var req = new CreateShiftRequest
        {
            EmployeeId = docAId,
            RoomId = "ROOM-101",
            ShiftDate = tomorrow.AddDays(7),
            ShiftType = "Morning",
            ShiftStart = "07:30",
            ShiftEnd = "11:30"
        };

        var res = await http.PostAsJsonAsync("/api/shifts", req);
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden for Doctor role, got {res.StatusCode}");
    });

    // =========================================================================
    // UC35.3: UPDATE SHIFT
    // =========================================================================

    await RunCheck("SEC-SHIFT-10 [UC35.3]: Sửa ca hợp lệ -> 200 OK & Ghi nhận Audit Trail (ISO 27789)", async () =>
    {
        SetAuth(http, managerToken);
        var req = new UpdateShiftRequest
        {
            EmployeeId = docAId,
            RoomId = "ROOM-101",
            ShiftDate = tomorrow.AddDays(5),
            ShiftType = "Afternoon",
            ShiftStart = "14:00", // Updated start time
            ShiftEnd = "17:30",   // Updated end time
            MaxPatients = 22,
            Notes = "Cap nhat gio lam viec"
        };

        var res = await http.PutAsJsonAsync($"/api/shifts/{createdShiftId}", req);
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");

        var dto = await res.Content.ReadFromJsonAsync<ShiftDto>();
        Assert(dto != null && dto.ShiftStart == "14:00" && dto.ShiftEnd == "17:30", "Shift time must be updated");

        // Verify Audit Trail
        var audit = await auditLogsCol.Find(a => a.Action == "UPDATE_SHIFT" && a.EntityId == createdShiftId).FirstOrDefaultAsync();
        Assert(audit != null, "Audit log for UPDATE_SHIFT must exist");
        Assert(audit!.Status == "SUCCESS", $"Status must be SUCCESS, got {audit.Status}");
        Assert(audit.Details.Contains("Updated shift"), "Details must capture update info");
    });

    await RunCheck("SEC-SHIFT-11 [UC35.3]: Sửa ca thành khung giờ chồng chéo lịch -> 409 Conflict", async () =>
    {
        SetAuth(http, managerToken);
        // Doctor B has shift SHIFT-DOC-B-01 tomorrow (07:30 - 11:30).
        // Try to update createdShiftId to tomorrow with overlapping time 08:00 - 10:00 for Doctor B.
        var req = new UpdateShiftRequest
        {
            EmployeeId = docBId,
            RoomId = "ROOM-102",
            ShiftDate = tomorrow,
            ShiftType = "Morning",
            ShiftStart = "08:00",
            ShiftEnd = "10:00",
            MaxPatients = 20
        };

        var res = await http.PutAsJsonAsync($"/api/shifts/{createdShiftId}", req);
        Assert(res.StatusCode == HttpStatusCode.Conflict, $"Expected 409 Conflict, got {res.StatusCode}");
    });

    await RunCheck("SEC-SHIFT-12 [UC35.3]: Sửa ca không tồn tại -> 404 Not Found", async () =>
    {
        SetAuth(http, managerToken);
        var req = new UpdateShiftRequest
        {
            EmployeeId = docAId,
            RoomId = "ROOM-101",
            ShiftDate = tomorrow.AddDays(10),
            ShiftType = "Morning",
            ShiftStart = "07:30",
            ShiftEnd = "11:30"
        };

        var res = await http.PutAsJsonAsync("/api/shifts/SHIFT-NONEXISTENT-9999", req);
        Assert(res.StatusCode == HttpStatusCode.NotFound, $"Expected 404 Not Found, got {res.StatusCode}");
    });

    await RunCheck("SEC-SHIFT-13 [UC35.3]: Sửa ca đã chốt/khóa (Status=Completed) -> 400 Bad Request (SHIFT_LOCKED)", async () =>
    {
        SetAuth(http, managerToken);
        var req = new UpdateShiftRequest
        {
            EmployeeId = docAId,
            RoomId = "ROOM-101",
            ShiftDate = tomorrow.AddDays(2),
            ShiftType = "Morning",
            ShiftStart = "08:00",
            ShiftEnd = "11:00"
        };

        var res = await http.PutAsJsonAsync("/api/shifts/SHIFT-LOCKED-01", req);
        Assert(res.StatusCode == HttpStatusCode.BadRequest, $"Expected 400 Bad Request for locked shift, got {res.StatusCode}");
    });

    // =========================================================================
    // UC35.4: VIEW SHIFT DETAILS
    // =========================================================================

    await RunCheck("SEC-SHIFT-14 [UC35.4]: Manager xem chi tiết ca trực theo ID -> 200 OK", async () =>
    {
        SetAuth(http, managerToken);
        var res = await http.GetAsync("/api/shifts/SHIFT-DOC-A-01");
        Assert(res.StatusCode == HttpStatusCode.OK, $"Expected 200 OK, got {res.StatusCode}");

        var dto = await res.Content.ReadFromJsonAsync<ShiftDto>();
        Assert(dto != null && dto.Id == "SHIFT-DOC-A-01", "Must return correct shift details");
    });

    await RunCheck("SEC-SHIFT-15 [UC35.4 IDOR]: Doctor A cố tình xem chi tiết ca trực của Doctor B -> 403 Forbidden & Ghi Audit Trail", async () =>
    {
        SetAuth(http, doctorAToken);
        // Doctor A calls endpoint with Doctor B's shift ID
        var res = await http.GetAsync("/api/shifts/SHIFT-DOC-B-01");
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden for unauthorized viewing of other's shift, got {res.StatusCode}");

        // Verify Audit Trail recorded warning
        var audit = await auditLogsCol.Find(a => a.Action == "VIEW_SHIFT_DETAILS" && a.Status == "ACCESS_DENIED" && a.UserId == docAId).FirstOrDefaultAsync();
        Assert(audit != null, "Audit entry for unauthorized view attempt must exist");
        Assert(audit!.Severity == "WARNING", $"Severity must be WARNING, got {audit.Severity}");
    });

    await RunCheck("SEC-SHIFT-16 [UC35.4]: Xem chi tiết ca trực với ID không tồn tại -> 404 Not Found", async () =>
    {
        SetAuth(http, managerToken);
        var res = await http.GetAsync("/api/shifts/SHIFT-NONEXISTENT-9999");
        Assert(res.StatusCode == HttpStatusCode.NotFound, $"Expected 404 Not Found, got {res.StatusCode}");
    });

    // =========================================================================
    // UC35.5: DELETE SHIFT
    // =========================================================================

    // Prepare shifts for deletion test cases:
    // 1. Unstarted and unused shift (tomorrow + 10 days, no bookings)
    var shiftUnused = new DoctorShift
    {
        Id = "SHIFT-DEL-UNUSED-01",
        DoctorId = docAId,
        EmployeeName = "Bac Si Nguyen Van A",
        RoomId = "ROOM-101",
        ShiftDate = tomorrow.AddDays(10),
        ShiftType = "Morning",
        ShiftStart = "07:30",
        ShiftEnd = "11:30",
        CurrentPatientsCount = 0,
        Status = "Approved"
    };

    // 2. Started/past shift (yesterday)
    var shiftPast = new DoctorShift
    {
        Id = "SHIFT-DEL-STARTED-01",
        DoctorId = docAId,
        EmployeeName = "Bac Si Nguyen Van A",
        RoomId = "ROOM-101",
        ShiftDate = fixedNow.UtcDateTime.Date.AddDays(-1),
        ShiftType = "Morning",
        ShiftStart = "07:30",
        ShiftEnd = "11:30",
        CurrentPatientsCount = 0,
        Status = "Approved"
    };

    // 3. Shift with active registered patients (CurrentPatientsCount = 5)
    var shiftWithPatients = new DoctorShift
    {
        Id = "SHIFT-DEL-PATIENTS-01",
        DoctorId = docAId,
        EmployeeName = "Bac Si Nguyen Van A",
        RoomId = "ROOM-101",
        ShiftDate = tomorrow.AddDays(11),
        ShiftType = "Morning",
        ShiftStart = "07:30",
        ShiftEnd = "11:30",
        CurrentPatientsCount = 5,
        Status = "Approved"
    };

    await shiftsCol.InsertManyAsync([shiftUnused, shiftPast, shiftWithPatients]);

    await RunCheck("SHIFT-DEL-01 [UC35.5]: Xóa ca chưa bắt đầu và chưa có dữ liệu liên quan -> Xóa cứng thành công (204 NoContent)", async () =>
    {
        SetAuth(http, managerToken);
        var res = await http.DeleteAsync("/api/shifts/SHIFT-DEL-UNUSED-01");
        Assert(res.StatusCode == HttpStatusCode.NoContent, $"Expected 204 NoContent, got {res.StatusCode}");

        // Verify hard deleted in DB
        var check = await shiftsCol.Find(s => s.Id == "SHIFT-DEL-UNUSED-01").FirstOrDefaultAsync();
        Assert(check == null, "Shift must be permanently deleted from database");
    });

    await RunCheck("SHIFT-DEL-02 [UC35.5]: Xóa ca đã bắt đầu/trong quá khứ -> Không xóa cứng, chuyển trạng thái sang Cancelled", async () =>
    {
        SetAuth(http, managerToken);
        var res = await http.DeleteAsync("/api/shifts/SHIFT-DEL-STARTED-01");
        Assert(res.StatusCode == HttpStatusCode.NoContent, $"Expected 204 NoContent, got {res.StatusCode}");

        // Verify not hard deleted, status changed to Cancelled
        var check = await shiftsCol.Find(s => s.Id == "SHIFT-DEL-STARTED-01").FirstOrDefaultAsync();
        Assert(check != null, "Shift must still exist in database");
        Assert(check!.Status == "Cancelled", $"Status must be 'Cancelled', got {check.Status}");
    });

    await RunCheck("SHIFT-DEL-03 [UC35.5]: Xóa ca đã có dữ liệu bệnh nhân đăng ký -> Không xóa cứng, chuyển sang Cancelled", async () =>
    {
        SetAuth(http, managerToken);
        var res = await http.DeleteAsync("/api/shifts/SHIFT-DEL-PATIENTS-01");
        Assert(res.StatusCode == HttpStatusCode.NoContent, $"Expected 204 NoContent, got {res.StatusCode}");

        // Verify not hard deleted, status changed to Cancelled
        var check = await shiftsCol.Find(s => s.Id == "SHIFT-DEL-PATIENTS-01").FirstOrDefaultAsync();
        Assert(check != null, "Shift must still exist in database");
        Assert(check!.Status == "Cancelled", $"Status must be 'Cancelled', got {check.Status}");
    });

    await RunCheck("SHIFT-DEL-04 [UC35.5]: Xóa Shift ID không tồn tại -> 404 Not Found", async () =>
    {
        SetAuth(http, managerToken);
        var res = await http.DeleteAsync("/api/shifts/SHIFT-NONEXISTENT-9999");
        Assert(res.StatusCode == HttpStatusCode.NotFound, $"Expected 404 Not Found, got {res.StatusCode}");
    });

    await RunCheck("SHIFT-DEL-05 [UC35.5]: Nhân viên không có quyền (Doctor/Patient) gọi xóa ca -> 403 Forbidden", async () =>
    {
        SetAuth(http, doctorAToken);
        var res = await http.DeleteAsync("/api/shifts/SHIFT-DOC-B-01");
        Assert(res.StatusCode == HttpStatusCode.Forbidden, $"Expected 403 Forbidden, got {res.StatusCode}");
    });

    await RunCheck("SHIFT-DEL-06 [UC35.5]: Xóa ca thành công -> Ghi nhận Audit Trail (ISO 27789)", async () =>
    {
        var audit = await auditLogsCol.Find(a => a.Action == "DELETE_SHIFT" && a.EntityId == "SHIFT-DEL-UNUSED-01").FirstOrDefaultAsync();
        Assert(audit != null, "Audit entry for DELETE_SHIFT must exist");
        Assert(audit!.Status == "SUCCESS", $"Status must be SUCCESS, got {audit.Status}");
        Assert(audit.Severity == "INFO", $"Severity must be INFO, got {audit.Severity}");
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
