// Đợt 3 step 1 checks: UC25/UC27/UC28 reception on real MongoDB.
// Q- numbers from an atomic counter (≥50 parallel, no duplicates), Q-999 exhaustion, daily reset, STAT + 1:1 order,
// conditional transitions, PAT-YYMMDD-XXXX codes (no CCCD inside), CCCD/phone validation, duplicate CCCD
// (also under a parallel race), masked messages, 401/403 by role. Writes only to a disposable database.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BMS.Api.Controllers.Reception;
using BMS.Api.Middleware;
using BMS.Application.DTOs.Patients;
using BMS.Application.DTOs.Reception;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
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
var databaseName = "BMS_UC27_Checks_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(databaseName);
// 2026-10-30 08:00 Asia/Saigon = 01:00 UTC
var clock = new MutableClock(new DateTimeOffset(2026, 10, 30, 1, 0, 0, TimeSpan.Zero));
var passed = 0;
var failed = 0;
try
{
    var indexes = new FlowIndexInitializer(db, NullLogger<FlowIndexInitializer>.Instance);
    await indexes.EnsureAsync(CancellationToken.None);
    var seq = new MongoSequenceGenerator(db);
    var tickets = db.GetCollection<QueueTicket>(QueueRepository.CollectionName);
    QueueService NewQueue() => new(new QueueRepository(db), seq, clock, NullLogger<QueueService>.Instance);
    var queue = NewQueue();

    await Check("no BmsDbContext / IMongoDatabase in QueueService, QueueRepository is Mongo-only", () =>
    {
        Assert(typeof(QueueService).GetConstructors().Single().GetParameters().All(p => p.ParameterType != typeof(BmsDbContext) && p.ParameterType != typeof(IMongoDatabase)));
        Assert(typeof(QueueRepository).GetConstructors().Single().GetParameters().All(p => p.ParameterType != typeof(BmsDbContext)));
        Assert(typeof(PatientService).GetConstructors().Single().GetParameters().All(p => p.ParameterType != typeof(BmsDbContext)));
        return Task.CompletedTask;
    });

    await Check("indexes exist: ux_queue_date_ticket (unique), ux_patients_cccd (unique)", async () =>
    {
        var q = await (await tickets.Indexes.ListAsync()).ToListAsync();
        Assert(q.Any(i => i["name"] == "ux_queue_date_ticket" && i.GetValue("unique", false).ToBoolean()));
        var p = await (await db.GetCollection<BsonDocument>("patients").Indexes.ListAsync()).ToListAsync();
        Assert(p.Any(i => i["name"] == "ux_patients_cccd" && i.GetValue("unique", false).ToBoolean()));
        await indexes.EnsureAsync(CancellationToken.None); // idempotent
    });

    await Check("first ticket of the day is Q-001", async () =>
    {
        var t = await queue.IssueTicketAsync(new IssueTicketRequestDto { PatientName = "Nguyễn Văn A" });
        Assert(t.TicketNumber == "Q-001" && t.Status == "Waiting" && t.CheckedInTime == "08:00");
    });

    await Check("60 parallel issues (separate service instances): 60 distinct numbers Q-002..Q-061, no gap", async () =>
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 60).Select(i => Task.Run(() =>
            NewQueue().IssueTicketAsync(new IssueTicketRequestDto { PatientName = "P" + i }))));
        var numbers = results.Select(r => r.TicketNumber).ToList();
        Assert(numbers.Distinct().Count() == 60);
        Assert(numbers.OrderBy(n => n).SequenceEqual(Enumerable.Range(2, 60).Select(i => QueueService.Format(i))));
        var day = await tickets.CountDocumentsAsync(t => t.QueueDate == "2026-10-30");
        Assert(day == 61);
        Console.WriteLine($"     concurrency: 60 parallel -> {numbers.Distinct().Count()} distinct, db rows {day}");
    });

    await Check("unique index rejects a forged duplicate Q- number for the same day", async () =>
    {
        try
        {
            await tickets.InsertOneAsync(new QueueTicket { TicketNumber = "Q-001", QueueDate = "2026-10-30", Sequence = 1 });
            throw new Exception("duplicate accepted");
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { }
    });

    await Check("Q-999 is the last number; the next issue is 409 MSG-ERR-REC-02 and never Q-1000", async () =>
    {
        await db.GetCollection<BsonDocument>("counters").UpdateOneAsync(new BsonDocument("_id", "queue:2026-10-30"),
            new BsonDocument("$set", new BsonDocument("seq", 998L)));
        var last = await queue.IssueTicketAsync(new IssueTicketRequestDto { PatientName = "Last" });
        Assert(last.TicketNumber == "Q-999");
        var ex = await Throws<QueueExhaustedException>(() => queue.IssueTicketAsync(new IssueTicketRequestDto { PatientName = "Over" }));
        Assert(ex.StatusCode == 409 && ex.Code == "MSG-ERR-REC-02");
        Assert(await tickets.CountDocumentsAsync(t => t.TicketNumber == "Q-1000") == 0);
        var board = await queue.GetLiveQueueBoardAsync();
        Assert(board.NextNumber == string.Empty);
    });

    await Check("daily reset: next local day (00:05 VN = 17:05 UTC previous day) starts again at Q-001", async () =>
    {
        clock.Set(new DateTimeOffset(2026, 10, 30, 17, 5, 0, TimeSpan.Zero));
        var t = await queue.IssueTicketAsync(new IssueTicketRequestDto { PatientName = "Mai" });
        Assert(t.TicketNumber == "Q-001");
        Assert(await tickets.CountDocumentsAsync(x => x.QueueDate == "2026-10-31") == 1);
    });

    await Check("call order (SMK-LT-03): STAT first, then Urgent/Routine 1:1 starting with priority, FCFS", async () =>
    {
        clock.Set(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero));
        var plan = new[] { QueuePriority.Routine, QueuePriority.Routine, QueuePriority.Urgent, QueuePriority.Routine,
            QueuePriority.Urgent, QueuePriority.Urgent, QueuePriority.STAT };
        foreach (var p in plan) await queue.IssueTicketAsync(new IssueTicketRequestDto { PatientName = "X", Priority = p });
        // Q-001 R, Q-002 R, Q-003 U, Q-004 R, Q-005 U, Q-006 U, Q-007 STAT
        var order = new List<string>();
        while (await queue.CallNextAsync("Quầy 1") is { } t) order.Add(t.TicketNumber);
        Assert(order.SequenceEqual(new[] { "Q-007", "Q-003", "Q-001", "Q-005", "Q-002", "Q-006", "Q-004" }), string.Join(",", order));
        Assert(await queue.CallNextAsync("Quầy 1") == null);
    });

    await Check("30 counters call-next in parallel on 20 waiting tickets: each ticket called exactly once", async () =>
    {
        clock.Set(new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero));
        for (var i = 0; i < 20; i++) await queue.IssueTicketAsync(new IssueTicketRequestDto { PatientName = "Y" + i });
        var calls = await Task.WhenAll(Enumerable.Range(0, 30).Select(i => Task.Run(async () =>
        {
            try { return await NewQueue().CallNextAsync("Quầy " + i); }
            catch (ConflictStateException) { return null; }
        })));
        var called = calls.Where(c => c != null).Select(c => c!.TicketNumber).ToList();
        Assert(called.Distinct().Count() == called.Count);
        var inDb = await tickets.CountDocumentsAsync(t => t.QueueDate == "2026-11-02" && t.Status == QueueTicketStatus.Called);
        Assert(inDb == called.Count && called.Count == 20, $"called={called.Count} inDb={inDb} nulls={calls.Count(c => c == null)}");
        Console.WriteLine($"     concurrency: 30 parallel call-next -> {called.Count} distinct calls, 0 double-calls");
    });

    await Check("transitions: Called->Completed ok, Completed->Called 409, Waiting->Collecting 409", async () =>
    {
        var t = await queue.IssueTicketAsync(new IssueTicketRequestDto { PatientName = "Z" });
        await Throws<ConflictStateException>(() => queue.UpdateTicketStatusAsync(t.Id, new UpdateTicketStatusRequestDto { Status = QueueTicketStatus.Collecting }));
        Assert(await queue.CallTicketAsync(t.Id, "Quầy 2"));
        Assert(await queue.UpdateTicketStatusAsync(t.Id, new UpdateTicketStatusRequestDto { Status = QueueTicketStatus.Completed }));
        var ex = await Throws<ConflictStateException>(() => queue.CallTicketAsync(t.Id, "Quầy 2"));
        Assert(ex.StatusCode == 409);
        Assert(!await queue.CallTicketAsync(ObjectId.GenerateNewId().ToString(), "Quầy 2"));
    });

    // ---------- patients (UC25) ----------
    clock.Set(new DateTimeOffset(2026, 10, 30, 1, 0, 0, TimeSpan.Zero));
    var patients = new PatientRepository(db);
    PatientService NewPatients() => new(new PatientRepository(db), Stub<IUserRepository>(), seq, clock, NullLogger<PatientService>.Instance);
    var patientService = NewPatients();
    CreateGuestProfileRequest Guest(string cccd, string phone, string name = "Trần Thị Bích") => new()
    { FullName = name, PhoneNumber = phone, IdentityNumber = cccd, DateOfBirth = new DateTime(1990, 5, 1), Gender = "Female" };

    await Check("patient code PAT-261030-0001, never contains the CCCD", async () =>
    {
        var p = await patientService.CreateGuestProfileAsync(Guest("079090001234", "0912345678"), "rec-1");
        Assert(p.Id == "PAT-261030-0001", p.Id);
        Assert(!p.Id.Contains("079090001234"));
    });

    await Check("CCCD must be exactly 12 digits; phone 10 digits 03/05/07/08/09 (service + DTO)", async () =>
    {
        foreach (var bad in new[] { "07909000123", "0790900012345", "07909000123A", "079090001" })
            Assert((await Throws<DomainValidationException>(() => patientService.CreateGuestProfileAsync(Guest(bad, "0912345678"), "r"))).Code == "MSG-ERR-REC-03");
        foreach (var bad in new[] { "0212345678", "091234567", "09123456789", "+84912345678", "0612345678" })
            Assert((await Throws<DomainValidationException>(() => patientService.CreateGuestProfileAsync(Guest("079090009999", bad), "r"))).Code == "MSG-ERR-REC-04");
        foreach (var ok in new[] { "0312345678", "0512345678", "0712345678", "0812345678", "0912345678" })
            Assert(PatientIdentityRules.Phone().IsMatch(ok));
        Assert(!Validates(Guest("07909000123", "0912345678")) && !Validates(Guest("079090001234", "0212345678")) && Validates(Guest("079090001234", "0912345678")));
    });

    await Check("duplicate CCCD -> 409 MSG-ERR-REC-01, message masks the CCCD and omits the name", async () =>
    {
        var ex = await Throws<PatientDuplicateException>(() => patientService.CreateGuestProfileAsync(Guest("079090001234", "0987654321", "Lê Văn C"), "r"));
        Assert(ex.StatusCode == 409 && ex.Code == "MSG-ERR-REC-01");
        Assert(!ex.Message.Contains("079090001234") && ex.Message.Contains("079******234") && !ex.Message.Contains("Bích"), ex.Message);
    });

    await Check("same CCCD registered by 10 counters in parallel: exactly 1 profile, 9 x MSG-ERR-REC-01", async () =>
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            try { await NewPatients().CreateGuestProfileAsync(Guest("079090005555", "0912345678", "Phạm Văn D" + i), "r"); return "ok"; }
            catch (PatientDuplicateException e) { return e.Code; }
        })));
        Assert(outcomes.Count(o => o == "ok") == 1 && outcomes.Count(o => o == "MSG-ERR-REC-01") == 9, string.Join(",", outcomes));
        Assert(await db.GetCollection<Patient>("patients").CountDocumentsAsync(p => p.IdentityCardNumber == "079090005555") == 1);
    });

    await Check("60 parallel new patients: 60 distinct PAT-261030-XXXX codes", async () =>
    {
        var created = await Task.WhenAll(Enumerable.Range(0, 60).Select(i => Task.Run(() =>
            NewPatients().CreateGuestProfileAsync(Guest("07909100" + i.ToString("D4"), "0912345678", "BN " + i), "r"))));
        var ids = created.Select(c => c.Id).ToList();
        Assert(ids.Distinct().Count() == 60 && ids.All(id => System.Text.RegularExpressions.Regex.IsMatch(id, @"^PAT-261030-\d{4}$")));
        Console.WriteLine($"     concurrency: 60 parallel patients -> {ids.Distinct().Count()} distinct codes");
    });

    // ---------- HTTP ----------
    await Check("HTTP queue: 401 anonymous; 403 Admin/Manager/Lab/Doctor/Patient; Receptionist 201/200", async () =>
    {
        clock.Set(new DateTimeOffset(2026, 11, 3, 1, 0, 0, TimeSpan.Zero));
        await using var app = await StartAsync(s =>
        {
            s.AddSingleton<IQueueService>(NewQueue());
            s.AddSingleton(Stub<IPatientReceptionService>());
        });
        using var http = Client(app);
        Assert((await http.PostAsJsonAsync("/api/reception/queue/issue", new { patientName = "A" })).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Admin", "Manager", "Lab", "Doctor", "Patient" })
        {
            Assert((await As(http, role).PostAsJsonAsync("/api/reception/queue/issue", new { patientName = "A" })).StatusCode == HttpStatusCode.Forbidden);
            Assert((await As(http, role).PostAsJsonAsync("/api/reception/queue/call-next", new { counterName = "Q1" })).StatusCode == HttpStatusCode.Forbidden);
        }
        var r = await As(http, "Receptionist").PostAsJsonAsync("/api/reception/queue/issue", new { patientName = "A" });
        Assert(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString());
        await app.StopAsync();
    });

    await Check("HTTP: 55 parallel POST /issue -> 55 distinct Q- numbers; exhausted day -> 409 code without exception text", async () =>
    {
        clock.Set(new DateTimeOffset(2026, 11, 4, 1, 0, 0, TimeSpan.Zero));
        await using var app = await StartAsync(s =>
        {
            s.AddScoped<IQueueService>(_ => NewQueue());
            s.AddSingleton(Stub<IPatientReceptionService>());
        });
        var bodies = await Task.WhenAll(Enumerable.Range(0, 55).Select(async i =>
        {
            using var h = Client(app);
            As(h, "Receptionist");
            var res = await h.PostAsJsonAsync("/api/reception/queue/issue", new { patientName = "H" + i });
            res.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        }));
        var nums = bodies.Select(b => Num(b)).ToList();
        Assert(nums.Distinct().Count() == 55, string.Join(",", nums.Take(5)));
        Console.WriteLine($"     concurrency: 55 parallel HTTP issues -> {nums.Distinct().Count()} distinct");
        await db.GetCollection<BsonDocument>("counters").UpdateOneAsync(new BsonDocument("_id", "queue:2026-11-04"),
            new BsonDocument("$set", new BsonDocument("seq", 999L)));
        using var http = Client(app);
        var over = await As(http, "Receptionist").PostAsJsonAsync("/api/reception/queue/issue", new { patientName = "Over" });
        var text = await over.Content.ReadAsStringAsync();
        Assert(over.StatusCode == HttpStatusCode.Conflict && text.Contains("MSG-ERR-REC-02") && !text.Contains("at BMS.") && !text.Contains("StackTrace"), text);
        await app.StopAsync();
    });

    await Check("HTTP walk-in: Receptionist only; duplicate CCCD -> 409 MSG-ERR-REC-01 (masked); bad CCCD -> 400", async () =>
    {
        await using var app = await StartAsync(s =>
        {
            s.AddSingleton<IWalkInService>(new WalkInService(NewPatients(), patients));
        });
        using var http = Client(app);
        object Body(string cccd, string phone) => new { fullName = "Võ Văn E", dateOfBirth = "1985-02-03", gender = "Male", phone, identityNumber = cccd };
        Assert((await http.PostAsJsonAsync("/api/walk-in/register", Body("079090007777", "0912345678"))).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Admin", "Manager", "Lab", "Doctor", "Patient" })
            Assert((await As(http, role).PostAsJsonAsync("/api/walk-in/register", Body("079090007777", "0912345678"))).StatusCode == HttpStatusCode.Forbidden);
        var ok = await As(http, "Receptionist").PostAsJsonAsync("/api/walk-in/register", Body("079090007777", "0912345678"));
        Assert(ok.StatusCode == HttpStatusCode.Created, ok.StatusCode + await ok.Content.ReadAsStringAsync());
        var dup = await As(http, "Receptionist").PostAsJsonAsync("/api/walk-in/register", Body("079090007777", "0912345678"));
        var dupText = await dup.Content.ReadAsStringAsync();
        Assert(dup.StatusCode == HttpStatusCode.Conflict && dupText.Contains("MSG-ERR-REC-01") && !dupText.Contains("079090007777"), dupText);
        Assert((await As(http, "Receptionist").PostAsJsonAsync("/api/walk-in/register", Body("079090777", "0912345678"))).StatusCode == HttpStatusCode.BadRequest);
        Assert((await As(http, "Receptionist").PostAsJsonAsync("/api/walk-in/register", Body("079090008888", "0212345678"))).StatusCode == HttpStatusCode.BadRequest);
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
    if (databaseName.StartsWith("BMS_UC27_Checks_", StringComparison.Ordinal)) await client.DropDatabaseAsync(databaseName);
}
Console.WriteLine($"{passed} PASSED, {failed} FAILED");
return failed == 0 ? 0 : 1;

async Task Check(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
static string Num(JsonElement e) => e.TryGetProperty("ticketNumber", out var v) ? v.GetString()! : e.GetProperty("data").GetProperty("ticketNumber").GetString()!;
static bool Validates(object o)
{
    var ctx = new System.ComponentModel.DataAnnotations.ValidationContext(o);
    return System.ComponentModel.DataAnnotations.Validator.TryValidateObject(o, ctx, new List<System.ComponentModel.DataAnnotations.ValidationResult>(), true);
}
static async Task<T> Throws<T>(Func<Task> f) where T : Exception
{
    try { await f(); }
    catch (T ex) { return ex; }
    throw new Exception($"expected {typeof(T).Name}");
}
static void Assert(bool condition, string? detail = null, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
{ if (!condition) throw new Exception($"Assertion failed at line {line}. {detail}"); }
static T Stub<T>() where T : class => DispatchProxy.Create<T, NotCalledProxy>();
static async Task<WebApplication> StartAsync(Action<IServiceCollection> register)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    builder.Services.AddControllers().AddApplicationPart(typeof(QueueController).Assembly);
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    register(builder.Services);
    builder.Services.AddAuthentication("Check").AddScheme<AuthenticationSchemeOptions, HeaderAuth>("Check", _ => { });
    builder.Services.AddAuthorization();
    var app = builder.Build();
    app.UseExceptionHandler();
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

/// <summary>Settable clock so day boundaries (Asia/Saigon) are deterministic.</summary>
sealed class MutableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public void Set(DateTimeOffset value) => _now = value;
    public override DateTimeOffset GetUtcNow() => _now;
}

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
