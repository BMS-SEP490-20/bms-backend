// DOT2 follow-up checks: UC12 Admin ops dashboard (ops data only), RoleId 9 -> 6 migration,
// AuditLog POST removed (UC64), UC19 package write = Admin + Manager.
// Writes only to a disposable database that is dropped at the end.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BMS.Api.Controllers;
using BMS.Api.Controllers.Reports;
using BMS.Api.Controllers.ServicePackages;
using BMS.Application.DTOs.Dashboard;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Entities;
using BMS.Infrastructure.Data;
using BMS.Infrastructure.Migrations;
using BMS.Infrastructure.Mongo;
using BMS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

MongoMappings.Register();
var mongoUri = Environment.GetEnvironmentVariable("BMS_CHECK_MONGO") ?? "mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=3000";
var client = new MongoClient(mongoUri);
var databaseName = "BMS_UC12_Checks_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(databaseName);
var clock = new FixedClock();
var now = clock.GetUtcNow().UtcDateTime;
var passed = 0;
var failed = 0;
try
{
    var accounts = db.GetCollection<User>("accounts");
    await accounts.InsertManyAsync(new[]
    {
        NewUser("a1", 1), NewUser("m1", 2), NewUser("l1", 3), NewUser("l2", 3), NewUser("d1", 4), NewUser("r1", 5),
        NewUser("p1", 6), NewUser("legacy1", 9), NewUser("legacy2", 9),
    });
    // locked: status Locked, and one with an active lockout window, one with an expired window
    await accounts.UpdateOneAsync(u => u.Id == "l2", Builders<User>.Update.Set(u => u.Status, (int)UserStatus.Locked));
    await accounts.UpdateOneAsync(u => u.Id == "d1", Builders<User>.Update.Set(u => u.LockoutEnd, now.AddMinutes(10)).Set(u => u.FailedLoginCount, 5));
    await accounts.UpdateOneAsync(u => u.Id == "r1", Builders<User>.Update.Set(u => u.LockoutEnd, now.AddMinutes(-10)));
    await accounts.UpdateOneAsync(u => u.Id == "a1", Builders<User>.Update.Set(u => u.LastLoginAt, now.AddHours(-1)));
    await accounts.UpdateOneAsync(u => u.Id == "m1", Builders<User>.Update.Set(u => u.LastLoginAt, now.AddDays(-3)));
    await accounts.UpdateOneAsync(u => u.Id == "p1", Builders<User>.Update.Set(u => u.LastLoginAt, now.AddDays(-30)));
    await db.GetCollection<Invoice>("invoices").InsertManyAsync(new[]
    {
        new Invoice { Id = ObjectId.GenerateNewId().ToString(), TotalAmount = 190_000m, Status = InvoiceStatus.Paid },
        new Invoice { Id = ObjectId.GenerateNewId().ToString(), TotalAmount = 76_000.5m, Status = InvoiceStatus.Paid },
        new Invoice { Id = ObjectId.GenerateNewId().ToString(), TotalAmount = 999_999m, Status = InvoiceStatus.Unpaid },
    });
    await db.GetCollection<BsonDocument>("audit_logs").InsertManyAsync(new[] { new BsonDocument("a", 1), new BsonDocument("a", 2), new BsonDocument("a", 3) });
    await db.GetCollection<IotDeviceThreshold>("iot_thresholds").InsertManyAsync(new[]
    {
        new IotDeviceThreshold { DeviceId = "TEMP-001", Status = "Active" }, new IotDeviceThreshold { DeviceId = "TEMP-002", Status = "Warning" },
        new IotDeviceThreshold { DeviceId = "TEMP-003", Status = "Inactive" },
    });
    await db.GetCollection<BackupRecord>("backup_records").InsertManyAsync(new[]
    {
        new BackupRecord { Status = BackupStatus.Completed, CreatedAt = now.AddDays(-2), CompletedAt = now.AddDays(-2).AddMinutes(3) },
        new BackupRecord { Status = BackupStatus.Failed, CreatedAt = now.AddDays(-1) },
        new BackupRecord { Status = BackupStatus.Completed, CreatedAt = now.AddDays(-20) },
    });
    // clinical collections that must never surface on the Admin dashboard
    await db.GetCollection<BsonDocument>("blood_samples").InsertOneAsync(new BsonDocument { { "_id", "S1" }, { "patient_name", "NGUYEN VAN A" } });

    var names = new MongoCollections("accounts", "roles", "refresh_tokens");
    var migration = new UserRoleMigration(db, names, clock, NullLogger<UserRoleMigration>.Instance);

    await Check("migration converts RoleId 9 -> 6 exactly once and records a marker", async () =>
    {
        Assert(await migration.MigrateLegacyPatientRoleIdAsync(CancellationToken.None) == 2);
        Assert(await accounts.CountDocumentsAsync(u => u.RoleId == 9) == 0);
        Assert(await accounts.CountDocumentsAsync(u => u.RoleId == 6) == 3);
        Assert(await migration.MigrateLegacyPatientRoleIdAsync(CancellationToken.None) == 0);
        var marker = await db.GetCollection<BsonDocument>("schema_migrations").Find(new BsonDocument("_id", UserRoleMigration.MigrationId)).SingleAsync();
        Assert(marker["convertedTotal"].ToInt64() == 2);
    });
    await Check("migration honours a cancelled token", async () =>
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        try { await migration.MigrateLegacyPatientRoleIdAsync(cts.Token); throw new Exception("no cancel"); }
        catch (OperationCanceledException) { }
    });

    var reader = new AdminOpsDashboardReader(db);
    await Check("ops snapshot: users per role, locked, logins, backup, audit rows, IoT, revenue", async () =>
    {
        var ops = await reader.ReadAsync(now, CancellationToken.None);
        Assert(ops.UsersPerRole["Admin"] == 1 && ops.UsersPerRole["Manager"] == 1 && ops.UsersPerRole["Lab"] == 2
            && ops.UsersPerRole["Doctor"] == 1 && ops.UsersPerRole["Receptionist"] == 1 && ops.UsersPerRole["Patient"] == 3);
        Assert(ops.UsersPerRole.Count == 6);
        Assert(ops.LockedAccounts == 2); // Locked status + active lockout; expired lockout not counted
        Assert(ops.Logins.DistinctAccountsLast24h == 1 && ops.Logins.DistinctAccountsLast7d == 2 && ops.Logins.AccountsWithFailedAttempts == 1);
        Assert(ops.Backup.LastStatus == "Failed" && ops.Backup.CompletedLast7d == 1 && ops.Backup.FailedLast7d == 1);
        Assert(ops.AuditLogRows == 3);
        Assert(ops.IotDevices.Online == 2 && ops.IotDevices.Offline == 1);
        Assert(ops.Revenue.TotalCollected == 266_000.5m && ops.Revenue.PaidInvoiceCount == 2);
    });
    await Check("ops DTO graph carries no patient/clinical field", () =>
    {
        var banned = new[] { "patient", "sample", "result", "panic", "name", "phone", "cccd", "bhyt" };
        foreach (var prop in AllProps(typeof(AdminOpsDashboardDto)))
            Assert(!banned.Any(b => prop.Contains(b, StringComparison.OrdinalIgnoreCase)) || prop == "PaidInvoiceCount");
        return Task.CompletedTask;
    });

    var dashboard = new DashboardService(new BmsDbContext(), db, reader, clock);
    await Check("GET stats for Admin returns ops cards only (no samples/orders)", async () =>
    {
        var stats = await dashboard.GetDashboardStatsAsync("a1", "Admin", CancellationToken.None);
        var json = JsonSerializer.Serialize(stats);
        Assert(stats.ManagerData == null && stats.PatientData == null && stats.DoctorData == null);
        Assert(!json.Contains("NGUYEN", StringComparison.Ordinal) && !json.Contains("samples\"", StringComparison.Ordinal));
        Assert(stats.SummaryCards.Select(c => c.Key).SequenceEqual(new[] { "users", "locked", "backup", "iot" }));
    });

    await Check("HTTP: admin-ops 401 anonymous, 403 for 5 non-Admin roles, 200 Admin", async () =>
    {
        await using var app = await StartAsync(s =>
        {
            s.AddSingleton<IDashboardService>(dashboard);
            s.AddSingleton(Stub<IServicePackageService>());
            s.AddSingleton(Stub<IAuditLogService>());
        });
        using var http = Client(app);
        Assert((await http.GetAsync("/api/dashboard/admin-ops")).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Manager", "Lab", "Doctor", "Receptionist", "Patient" })
            Assert((await As(http, role).GetAsync("/api/dashboard/admin-ops")).StatusCode == HttpStatusCode.Forbidden);
        var ok = await As(http, "Admin").GetAsync("/api/dashboard/admin-ops");
        Assert(ok.StatusCode == HttpStatusCode.OK);
        var body = await ok.Content.ReadAsStringAsync();
        Assert(body.Contains("usersPerRole") && body.Contains("auditLogRows") && !body.Contains("NGUYEN"));
        await app.StopAsync();
    });
    await Check("HTTP: POST TestOrder/AuditLog no longer exists (UC64 audit is system-written)", async () =>
    {
        await using var app = await StartAsync(s =>
        {
            s.AddSingleton<IDashboardService>(dashboard);
            s.AddSingleton(Stub<IServicePackageService>());
            s.AddSingleton(Stub<IAuditLogService>());
        });
        using var http = Client(app);
        var res = await As(http, "Admin").PostAsJsonAsync("/TestOrder/AuditLog", new { action = "forged" });
        Assert(res.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
        Assert(typeof(AuditLogController).GetMethods().All(m => m.GetCustomAttributes().All(a => a.GetType().Name != "HttpPostAttribute")));
        await app.StopAsync();
    });
    await Check("HTTP: UC19 package write — Admin and Manager pass auth, Lab/Doctor/Receptionist/Patient 403", async () =>
    {
        await using var app = await StartAsync(s =>
        {
            s.AddSingleton<IDashboardService>(dashboard);
            s.AddSingleton(Stub<IServicePackageService>());
            s.AddSingleton(Stub<IAuditLogService>());
        });
        using var http = Client(app);
        Assert((await http.PostAsJsonAsync("/api/service-packages", new { })).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Lab", "Doctor", "Receptionist", "Patient" })
            Assert((await As(http, role).PostAsJsonAsync("/api/service-packages", new { })).StatusCode == HttpStatusCode.Forbidden);
        // empty body => model validation 400, i.e. authorization passed
        foreach (var role in new[] { "Admin", "Manager" })
            Assert((await As(http, role).PostAsJsonAsync("/api/service-packages", new { })).StatusCode == HttpStatusCode.BadRequest);
        await app.StopAsync();
    });
}
catch (Exception ex)
{
    failed++;
    Console.WriteLine($"FAIL {ex.Message}\n{ex.StackTrace}");
}
finally
{
    if (databaseName.StartsWith("BMS_UC12_Checks_", StringComparison.Ordinal)) await client.DropDatabaseAsync(databaseName);
}
Console.WriteLine($"{passed} PASSED, {failed} FAILED");
return failed == 0 ? 0 : 1;

User NewUser(string id, int roleId) => new() { Id = id, Username = id, Email = id + "@bms.health.vn", RoleId = roleId, FullName = "U " + id };
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine($"PASS {name}"); }
static void Assert(bool condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { if (!condition) throw new Exception($"Assertion failed at line {line}."); }
static IEnumerable<string> AllProps(Type t, int depth = 0)
{
    if (depth > 3) yield break;
    foreach (var p in t.GetProperties())
    {
        yield return p.Name;
        var pt = p.PropertyType;
        if (pt.Namespace?.StartsWith("BMS", StringComparison.Ordinal) == true)
            foreach (var n in AllProps(pt, depth + 1)) yield return n;
    }
}
static T Stub<T>() where T : class => DispatchProxy.Create<T, NotCalledProxy>();
static async Task<WebApplication> StartAsync(Action<IServiceCollection> register)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    builder.Services.AddControllers().AddApplicationPart(typeof(DashboardController).Assembly);
    register(builder.Services);
    builder.Services.AddAuthentication("Check").AddScheme<AuthenticationSchemeOptions, HeaderAuth>("Check", _ => { });
    builder.Services.AddAuthorization();
    var app = builder.Build();
    app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
    await app.StartAsync();
    return app;
}
static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.Single()) };
static HttpClient As(HttpClient http, string role)
{
    http.DefaultRequestHeaders.Remove("X-Check-Role"); http.DefaultRequestHeaders.Add("X-Check-Role", role);
    return http;
}

/// <summary>Fixed clock so time windows in the checks are deterministic.</summary>
sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 30, 3, 0, 0, TimeSpan.Zero); }

/// <summary>Proxy for services the checked endpoints must not reach.</summary>
public class NotCalledProxy : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new InvalidOperationException($"{targetMethod?.Name} must not be called by this check.");
}

/// <summary>Authenticates from the X-Check-Role header; no header means anonymous.</summary>
sealed class HeaderAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["X-Check-Role"].ToString();
        if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "check-" + role), new Claim("sub", "check-" + role), new Claim(ClaimTypes.Role, role) }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
