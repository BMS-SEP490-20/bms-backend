// Đợt 3 step 4 checks: specimen collection (TIEU-CHI Đợt 3 Bước 3, SMK-LAB-01..04): payment gate, 2-factor
// patient check, SMP-YYYYMMDD-XXXX barcodes (unique, >=50 parallel), CLSI tube colors, single winner, 401/403.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using BMS.Api.Controllers.Specimens;
using BMS.Api.Middleware;
using BMS.Application.DTOs.Specimens;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
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
var databaseName = "BMS_UC39_Checks_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(databaseName);
var clock = new MutableClock(new DateTimeOffset(2026, 10, 30, 2, 0, 0, TimeSpan.Zero));
var passed = 0;
var failed = 0;
try
{
    await new FlowIndexInitializer(db, NullLogger<FlowIndexInitializer>.Instance).EnsureAsync(CancellationToken.None);
    await db.GetCollection<Patient>("patients").InsertOneAsync(new Patient
    { Id = "PAT-261030-0001", FullName = "Trần Thị Bích", DateOfBirth = new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc), GenderString = "Female", IdentityCardNumber = "079090001234", PhoneNumber = "0912345678" });
    var orderCol = db.GetCollection<TestOrder>(TestOrderRepository.CollectionName);
    var sampleCol = db.GetCollection<Sample>(SpecimenRepository.CollectionName);
    var counter = 0;
    async Task<TestOrder> NewOrder(TestOrderStatus status = TestOrderStatus.PaidPendingSample, bool deferred = false, params (string Code, string Name)[] tests)
    {
        if (tests.Length == 0) tests = new[] { ("GLU", "Glucose máu"), ("CBC", "Công thức máu (CBC)"), ("BIO", "Sinh hóa máu") };
        var o = new TestOrder
        {
            OrderNumber = $"ORD-20261030-{Interlocked.Increment(ref counter):D4}", PatientId = "PAT-261030-0001", PatientName = "Trần Thị Bích",
            DoctorId = "doc-1", Status = status, PaymentDeferred = deferred, Icd10Codes = new() { "E11.9" },
            Items = tests.Select(t => new TestOrderItem { TestCode = t.Code, TestName = t.Name, Category = "", Price = 100_000m }).ToList()
        };
        await orderCol.InsertOneAsync(o);
        return o;
    }
    var seq = new MongoSequenceGenerator(db);
    SpecimenCollectionService NewSvc() => new(new TestOrderRepository(db), new PatientRepository(db), new SpecimenRepository(db), seq, clock, NullLogger<SpecimenCollectionService>.Instance);
    var svc = NewSvc();
    CollectSpecimenRequestDto Req(string orderId, string name = "Trần Thị Bích", DateOnly? dob = null, string? cccd = null) =>
        new() { OrderId = orderId, FullName = name, DateOfBirth = dob ?? (cccd == null ? new DateOnly(1990, 5, 1) : null), IdentityNumber = cccd };

    await Check("paid order + 2 factors -> SMP-20261030-0001.., one tube per color in draw order, Glucose Gray, CBC Purple", async () =>
    {
        var o = await NewOrder();
        var r = await svc.CollectAsync(Req(o.OrderNumber), "lab-1");
        Assert(r.OrderStatus == "SampleCollected" && r.Tubes.Count == 3);
        Assert(r.Tubes.Select(t => t.TubeColor).SequenceEqual(new[] { "Red", "Purple", "Gray" }), string.Join(",", r.Tubes.Select(t => t.TubeColor)));
        Assert(r.Tubes.Single(t => t.Tests.Contains("GLU")).TubeColor == "Gray" && r.Tubes.Single(t => t.Tests.Contains("CBC")).TubeColor == "Purple");
        Assert(r.Tubes.All(t => Regex.IsMatch(t.Barcode, @"^SMP-20261030-\d{4}$")) && r.Tubes[0].Barcode == "SMP-20261030-0001");
        Assert(r.LabelPatientName == "TRẦN THỊ BÍCH" && r.BirthYear == 1990 && r.CollectedBy == "lab-1");
        var stored = await sampleCol.Find(s => s.TestOrderId == o.OrderNumber).ToListAsync();
        Assert(stored.Count == 3 && stored.All(s => s.Status == SampleStatus.Collected && s.CollectedBy == "lab-1" && s.CollectedAt.Kind == DateTimeKind.Utc));
    });

    await Check("payment gate: Routine AwaitingPayment -> 409 ORDER_NOT_PAID, nothing written; STAT deferred -> allowed", async () =>
    {
        var unpaid = await NewOrder(TestOrderStatus.AwaitingPayment);
        var before = await sampleCol.CountDocumentsAsync(FilterDefinition<Sample>.Empty);
        Assert((await Throws<ConflictStateException>(() => svc.CollectAsync(Req(unpaid.Id), "lab-1"))).Code == "ORDER_NOT_PAID");
        Assert(await sampleCol.CountDocumentsAsync(FilterDefinition<Sample>.Empty) == before);
        var stat = await NewOrder(TestOrderStatus.AwaitingPayment, deferred: true);
        Assert((await svc.CollectAsync(Req(stat.Id), "lab-1")).OrderStatus == "SampleCollected");
        Assert((await Throws<ConflictStateException>(() => svc.CollectAsync(Req(stat.Id), "lab-1"))).Code == "SAMPLE_ALREADY_COLLECTED");
        var cancelled = await NewOrder(TestOrderStatus.Cancelled);
        Assert((await Throws<ConflictStateException>(() => svc.CollectAsync(Req(cancelled.Id), "lab-1"))).Code == "ORDER_CANCELLED");
    });

    await Check("2 factors: wrong name / wrong DOB / wrong CCCD / no 2nd factor -> 400; diacritics+case-insensitive name; CCCD factor ok", async () =>
    {
        var o = await NewOrder();
        foreach (var bad in new[] { Req(o.Id, "Trần Thị Bính"), Req(o.Id, dob: new DateOnly(1990, 5, 2)), Req(o.Id, cccd: "079090009999"), new CollectSpecimenRequestDto { OrderId = o.Id, FullName = "Trần Thị Bích" } })
            Assert((await Throws<DomainValidationException>(() => svc.CollectAsync(bad, "lab-1"))).Code == "SAMPLE_PATIENT_MISMATCH");
        Assert((await orderCol.Find(x => x.Id == o.Id).SingleAsync()).Status == TestOrderStatus.PaidPendingSample);
        Assert((await svc.CollectAsync(Req(o.Id, "  tran thi  bich ", cccd: "079090001234"), "lab-1")).OrderStatus == "SampleCollected");
    });

    await Check("60 orders collected in parallel -> all SMP- barcodes distinct (unique index ux_blood_samples_smp_barcode)", async () =>
    {
        clock.Set(new DateTimeOffset(2026, 10, 31, 2, 0, 0, TimeSpan.Zero));
        var list = new List<TestOrder>();
        for (var i = 0; i < 60; i++) list.Add(await NewOrder(tests: new[] { ("CBC", "CBC"), ("GLU", "Glucose") }));
        var res = await Task.WhenAll(list.Select(o => Task.Run(() => NewSvc().CollectAsync(Req(o.Id), "lab-" + o.OrderNumber))));
        var codes = res.SelectMany(r => r.Tubes.Select(t => t.Barcode)).ToList();
        Assert(codes.Count == 120 && codes.Distinct().Count() == 120 && codes.All(c => c.StartsWith("SMP-20261031-", StringComparison.Ordinal)));
        Console.WriteLine($"     concurrency: 60 parallel collections -> {codes.Distinct().Count()} distinct barcodes / {codes.Count}");
        try
        {
            await sampleCol.InsertOneAsync(new Sample { Barcode = codes[0], SmpDay = "2026-10-31" });
            throw new Exception("duplicate barcode accepted");
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { }
    });

    await Check("same order collected by 10 Lab users at once -> exactly 1 success, tubes written once", async () =>
    {
        var o = await NewOrder();
        var res = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            try { await NewSvc().CollectAsync(Req(o.Id), "lab-" + i); return "ok"; }
            catch (ConflictStateException) { return "409"; }
        })));
        Assert(res.Count(r => r == "ok") == 1, string.Join(",", res));
        Assert(await sampleCol.CountDocumentsAsync(s => s.TestOrderId == o.OrderNumber) == 3);
    });

    await Check("HTTP collect: 401 anon; 403 Admin/Manager/Doctor/Receptionist/Patient; Lab 200 (collector from token); unpaid 409", async () =>
    {
        await using var app = await StartAsync(s => s.AddScoped<ISpecimenCollectionService>(_ => NewSvc()));
        using var http = Client(app);
        var o = await NewOrder();
        var body = new { orderId = o.Id, fullName = "Trần Thị Bích", dateOfBirth = "1990-05-01" };
        Assert((await http.PostAsJsonAsync("/api/specimens/collect", body)).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Admin", "Manager", "Doctor", "Receptionist", "Patient" })
            Assert((await As(http, role).PostAsJsonAsync("/api/specimens/collect", body)).StatusCode == HttpStatusCode.Forbidden, role);
        var ok = await As(http, "Lab").PostAsJsonAsync("/api/specimens/collect", body);
        var text = await ok.Content.ReadAsStringAsync();
        Assert(ok.StatusCode == HttpStatusCode.OK && text.Contains("\"collectedBy\":\"check-Lab\"") && text.Contains("SMP-"), text);
        var unpaid = await NewOrder(TestOrderStatus.AwaitingPayment);
        var c = await As(http, "Lab").PostAsJsonAsync("/api/specimens/collect", new { orderId = unpaid.Id, fullName = "Trần Thị Bích", dateOfBirth = "1990-05-01" });
        Assert(c.StatusCode == HttpStatusCode.Conflict && (await c.Content.ReadAsStringAsync()).Contains("ORDER_NOT_PAID"));
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
    if (databaseName.StartsWith("BMS_UC39_Checks_", StringComparison.Ordinal)) await client.DropDatabaseAsync(databaseName);
}
Console.WriteLine($"{passed} PASSED, {failed} FAILED");
return failed == 0 ? 0 : 1;

async Task Check(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
static async Task<T> Throws<T>(Func<Task> f) where T : Exception
{
    try { await f(); }
    catch (T ex) { return ex; }
    throw new Exception($"expected {typeof(T).Name}");
}
static void Assert(bool condition, string? detail = null, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
{ if (!condition) throw new Exception($"Assertion failed at line {line}. {detail}"); }
static async Task<WebApplication> StartAsync(Action<IServiceCollection> register)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    builder.Services.AddControllers().AddApplicationPart(typeof(SpecimenCollectionController).Assembly);
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

