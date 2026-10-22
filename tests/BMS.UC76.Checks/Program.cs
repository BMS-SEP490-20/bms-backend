using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BMS.Api.Controllers.Laboratory;
using BMS.Api.Middleware;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

var client = new MongoClient("mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=3000");
var name = "BMS_UC76_Checks_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(name); var passed = 0;
try
{
    var repository = new TestHistoryRepository(db); var service = new TestHistoryService(repository, TimeProvider.System);
    var account = ObjectId.GenerateNewId(); var mixed = ObjectId.GenerateNewId();
    var patient = new TestHistoryViewer(account.ToString(), "Patient", "127.0.0.1"); var staff = new TestHistoryViewer("staff", "LabHead");
    var results = db.GetCollection<BsonDocument>("test_results"); var samples = db.GetCollection<BsonDocument>("blood_samples");
    await db.GetCollection<BsonDocument>("patients").InsertManyAsync(new[] {
        new BsonDocument { { "_id", "P1" }, { "AccountId", account } }, new BsonDocument { { "_id", "P2" }, { "AccountId", "other" } }
    });
    await samples.InsertManyAsync(new[] { new BsonDocument { { "_id", "S1" }, { "patient_id", "P1" }, { "status", "Completed" } }, new BsonDocument { { "_id", "S2" }, { "patient_id", "P2" }, { "status", "Completed" } } });
    await db.GetCollection<BsonDocument>("test_packages").InsertOneAsync(new BsonDocument { { "_id", "PK1" }, { "package_name", "Fixture panel" } });
    BsonDocument Result(string id, string person, string sample, bool signed) => new() { { "_id", id }, { "patient_id", person }, { "sample_id", sample }, { "package_id", "PK1" }, { "status", "APPROVED" }, { "digital_signature", signed ? "fixture-signature" : "" }, { "conclusion_notes", "fixture-sensitive-conclusion" } };
    var r1 = Result("R[1]", "P1", "S1", true); r1["result_code"] = "RES-[1]"; r1["created_at"] = new DateTime(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);
    var r2 = Result("R2", "P1", "S1", false); r2["created_at"] = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
    var conflict = Result("CONFLICT", "P2", "S2", true); conflict["patientId"] = "P1";
    await results.InsertManyAsync(new[] { r1, r2, Result("FOREIGN", "P2", "S2", true), conflict,
        new BsonDocument { { "_id", mixed }, { "patientId", "P1" }, { "sampleId", "S1" }, { "packageId", "PK1" }, { "validationStatus", 3 }, { "digitalSignature", "fixture-signature" },
            { "items", new BsonArray { new BsonDocument { { "criterionCode", "FIX" }, { "criterionName", "Fixture marker" }, { "measuredValue", "<5" }, { "unit", "fixture-unit" }, { "referenceInterval", "stored range" }, { "flag", 2 } } } } }
    });
    await db.GetCollection<BsonDocument>("test_parameters").InsertOneAsync(new BsonDocument { { "result_id", "R[1]" }, { "name", "Fixture parameter" }, { "code", "F1" }, { "value_numeric", 3.14 }, { "unit", "fixture-unit" }, { "reference_display", "stored range" }, { "flag", "H" } });
    var before = (await results.Find(new BsonDocument()).Sort(new BsonDocument("_id", 1)).ToListAsync()).ToJson();
    await Check("ownership, canonical identity precedence, escaped search and stable pagination", async () =>
    {
        var own = await service.ListAsync(new(), patient); Assert(own.Total == 3 && own.Items.All(x => x.PatientId == "P1"));
        Assert((await service.ListAsync(new() { PatientId = "P2" }, patient)).Total == 0);
        Assert((await service.ListAsync(new() { Search = "[1]" }, patient)).Total == 1);
        var a = await service.ListAsync(new() { PageSize = 1 }, patient); var b = await service.ListAsync(new() { Page = 2, PageSize = 1 }, patient);
        Assert(a.Items[0].Id != b.Items[0].Id && a.TotalPages == 3);
        Assert(await service.GetAsync("FOREIGN", patient) == null && await service.GetAsync("CONFLICT", patient) == null);
        Assert((await service.ListAsync(new(), new("unlinked", "Patient"))).Total == 0);
    });
    await Check("clinic creation-date filters include both endpoints and exclude undated results", async () =>
    {
        Assert((await service.ListAsync(new() { FromDate = new(2026, 10, 22), ToDate = new(2026, 10, 22) }, patient)).Items.Single().Id == "R[1]");
        Assert((await service.ListAsync(new() { ToDate = new(2026, 10, 22) }, patient)).Total == 1);
    });
    await Check("signed legacy/camel details reuse stored values and never invent dates or ranges", async () =>
    {
        var first = (await service.GetAsync("R[1]", patient))!;
        Assert(first.Summary.Available && first.Items.Single().Value == "3.14" && first.Items[0].ReferenceRange == "stored range" && first.Summary.ApprovedAt == null);
        var second = (await service.GetAsync(mixed.ToString(), patient))!;
        Assert(second.Summary.Available && second.Summary.CreatedAt == null && second.Items.Single().Value == "<5");
    });
    await Check("unsigned or rerun/revoked/conflicting states never disclose values even to staff", async () =>
    {
        var pending = (await service.GetAsync("R2", staff))!; Assert(!pending.Summary.Available && pending.Items.Count == 0 && pending.Conclusion == null);
        foreach (var patch in new[] { new BsonDocument("rerun_requested", true), new BsonDocument("validationStatus", 1), new BsonDocument("status", "Pending") })
        {
            await results.UpdateOneAsync(new BsonDocument("_id", "R[1]"), new BsonDocument("$set", patch));
            var hidden = (await service.GetAsync("R[1]", patient))!; Assert(!hidden.Summary.Available && hidden.Items.Count == 0 && hidden.Conclusion == null);
            await results.ReplaceOneAsync(new BsonDocument("_id", "R[1]"), r1);
        }
        await samples.UpdateOneAsync(new BsonDocument("_id", "S1"), new BsonDocument("$set", new BsonDocument("status", "RerunRequested")));
        Assert(!(await service.GetAsync("R[1]", patient))!.Summary.Available);
        await samples.UpdateOneAsync(new BsonDocument("_id", "S1"), new BsonDocument("$set", new BsonDocument("status", "Completed")));
    });
    await Check("access audit records actor/action/target/time without changing clinical documents", async () =>
    {
        var audit = db.GetCollection<BsonDocument>("audit_logs"); Assert(await audit.CountDocumentsAsync(new BsonDocument("Module", "UC76")) > 0);
        Assert(await audit.Find(new BsonDocument { { "AccountId", account }, { "Action", "ViewTestHistoryDetail" }, { "Details", "R[1]" } }).AnyAsync());
        Assert(before == (await results.Find(new BsonDocument()).Sort(new BsonDocument("_id", 1)).ToListAsync()).ToJson());
    });
    await Check("invalid filters and unsupported roles fail closed", async () =>
    {
        await Invalid(() => service.ListAsync(new() { PageSize = 101 }, patient));
        await Invalid(() => service.ListAsync(new() { FromDate = new(2026, 10, 23), ToDate = new(2026, 10, 22) }, patient));
        await Invalid(() => service.GetAsync(" ", patient));
        foreach (var role in new[] { "Receptionist", "Nurse", "Cashier", "WarehouseStaff" }) await Denied(() => service.ListAsync(new(), new("staff", role)));
    });
    await Check("HTTP roles, no-store, validation, ownership and read-only methods", async () =>
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(TestHistoryController).Assembly);
        builder.Services.AddSingleton<ITestHistoryRepository>(repository); builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddScoped<ITestHistoryService, TestHistoryService>();
        builder.Services.AddAuthentication("UC76").AddScheme<AuthenticationSchemeOptions, HistoryAuth>("UC76", _ => { }); builder.Services.AddAuthorization();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>(); builder.Services.AddProblemDetails();
        await using var app = builder.Build(); app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert((await http.GetAsync("/api/test-history")).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Nurse", "Receptionist", "Cashier", "WarehouseStaff" }) { Role(role); Assert((await http.GetAsync("/api/test-history")).StatusCode == HttpStatusCode.Forbidden); }
        foreach (var role in new[] { "Admin", "Administrator", "Manager", "Doctor", "LabHead", "LabTech", "LabTechnician" }) { Role(role); using var response = await http.GetAsync("/api/test-history"); Assert(response.StatusCode == HttpStatusCode.OK && response.Headers.CacheControl?.NoStore == true); }
        Role("Patient"); http.DefaultRequestHeaders.Add("X-User", account.ToString());
        Assert((await http.GetFromJsonAsync<TestHistoryPage>("/api/test-history?patientId=P2&accountId=other"))?.Total == 0);
        Assert((await http.GetAsync("/api/test-history/FOREIGN")).StatusCode == HttpStatusCode.NotFound);
        Assert((await http.GetAsync("/api/test-history?page=0")).StatusCode == HttpStatusCode.BadRequest);
        Assert((await http.GetAsync("/api/test-history?fromDate=bad")).StatusCode == HttpStatusCode.BadRequest);
        Assert((await http.PostAsJsonAsync("/api/test-history", new { })).StatusCode == HttpStatusCode.MethodNotAllowed);
        await app.StopAsync();
        void Role(string role) { http.DefaultRequestHeaders.Remove("X-Role"); http.DefaultRequestHeaders.Add("X-Role", role); }
    });
    Console.WriteLine($"UC76: {passed} check groups passed.");
}
finally
{
    if (!name.StartsWith("BMS_UC76_Checks_", StringComparison.Ordinal) || name.Length != 48) throw new InvalidOperationException("Unexpected fixture database name.");
    await client.DropDatabaseAsync(name);
}
async Task Check(string title, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + title); }
static void Assert(bool value) { if (!value) throw new Exception("Assertion failed."); }
static async Task Invalid(Func<Task> action) { try { await action(); } catch (DomainValidationException) { return; } throw new Exception("Expected validation error."); }
static async Task Denied(Func<Task> action) { try { await action(); } catch (ForbiddenAccessException) { return; } throw new Exception("Expected denial."); }
sealed class HistoryAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["X-Role"].ToString(); if (role.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim("sub", Request.Headers["X-User"].FirstOrDefault() ?? "staff"), new Claim("role", role) }, Scheme.Name, "name", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
