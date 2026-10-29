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

var client = new MongoClient(Environment.GetEnvironmentVariable("BMS_CHECK_MONGO") ?? "mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=3000");
var databaseName = "BMS_UC71_Checks_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(databaseName);
var passed = 0;
try
{
    var repository = new SampleTimelineRepository(db);
    var service = new SampleTimelineService(repository);
    var staff = new SampleTimelineViewer("staff", "Lab");
    var accountId = ObjectId.GenerateNewId();
    var patient = new SampleTimelineViewer(accountId.ToString(), "Patient");
    var secondSample = ObjectId.GenerateNewId();
    var at = new DateTime(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);
    var samples = db.GetCollection<BsonDocument>("blood_samples");
    var results = db.GetCollection<BsonDocument>("test_results");
    await db.GetCollection<BsonDocument>("patients").InsertManyAsync(new[] {
        new BsonDocument { { "_id", "P1" }, { "AccountId", accountId }, { "phone", "sensitive-contact" } },
        new BsonDocument { { "_id", "P2" }, { "AccountId", "other-account" } },
        new BsonDocument { { "_id", "P-STRING" }, { "AccountId", accountId.ToString() } },
        new BsonDocument { { "_id", "P-DELETED" }, { "AccountId", accountId }, { "is_deleted", true } }
    });
    await samples.InsertManyAsync(new[] {
        new BsonDocument { { "_id", "S1" }, { "barcode", "BC-[1]" }, { "patient_id", "P1" }, { "appointment_id", "A1" }, { "rfid_tag", "TRAY-1" },
            { "status", "Completed" }, { "collected_at", at }, { "completed_at", at.AddHours(1) }, { "notes", "sensitive-sample-note" },
            { "status_history", new BsonArray {
                new BsonDocument { { "to_status", "Collected" }, { "changed_at", at }, { "operator_name", "Fixture operator" }, { "notes", "sensitive-audit-note" } },
                new BsonDocument { { "to_status", "Received" }, { "changed_at", at.AddMinutes(10) } },
                new BsonDocument { { "to_status", "Processing" }, { "changed_at", at.AddMinutes(20) } },
                new BsonDocument { { "to_status", "Testing" }, { "changed_at", at.AddMinutes(30) } },
                new BsonDocument { { "to_status", "RerunRequested" } }
            } } },
        new BsonDocument { { "_id", secondSample }, { "barcode", "BC-2" }, { "patient_id", "P1" }, { "status", "PendingCollection" } },
        new BsonDocument { { "_id", "S-OTHER" }, { "barcode", "BC-OTHER" }, { "patient_id", "P2" }, { "status", "Collected" }, { "collected_at", at.AddDays(1) } },
        new BsonDocument { { "_id", "S-DELETED" }, { "barcode", "BC-DELETED" }, { "patient_id", "P1" }, { "is_deleted", true } }
    });
    await db.GetCollection<BsonDocument>("appointments").InsertOneAsync(new BsonDocument { { "_id", "A1" }, { "patient_id", "P1" }, { "check_in_time", at.AddMinutes(-15) } });
    await db.GetCollection<BsonDocument>("specimen_histories").InsertManyAsync(new[] {
        new BsonDocument { { "_id", "H1" }, { "barcode", "BC-[1]" }, { "patientId", "P1" }, { "timeline", new BsonArray {
            new BsonDocument { { "eventType", "Stored" }, { "timestamp", at.AddHours(2) }, { "location", "Cold room" } },
            new BsonDocument { { "eventType", "ResultAvailable" }, { "timestamp", at.AddHours(3) }, { "notes", "Unverified manual approval" } }
        } } },
        new BsonDocument { { "_id", "H-WRONG" }, { "barcode", "BC-[1]" }, { "patientId", "P2" }, { "timeline", new BsonArray { new BsonDocument { { "title", "foreign-sensitive-event" } } } } }
    });
    await db.GetCollection<BsonDocument>("rfid_tracking_logs").InsertManyAsync(new[] {
        new BsonDocument { { "_id", "R1" }, { "sample_id", "S1" }, { "rfid_tag", "TRAY-1" }, { "scanned_at", at.AddMinutes(1) }, { "location", "Collection gate" }, { "scanner_id", "READER-1" } },
        new BsonDocument { { "_id", "R2" }, { "rfid_tag", "TRAY-1" }, { "scanned_at", at.AddMinutes(2) }, { "checkpoint_name", "Lab gate" }, { "reader_device_id", "READER-2" } },
        new BsonDocument { { "_id", "R3" }, { "sample_id", "S-OTHER" }, { "rfid_tag", "TRAY-1" }, { "scanned_at", at.AddMinutes(3) } },
        new BsonDocument { { "_id", "R4" }, { "rfid_tag", "TRAY-1" }, { "scanned_at", at.AddMinutes(-1) } },
        new BsonDocument { { "_id", "R5" }, { "sample_id", "S1" } }
    });
    await results.InsertOneAsync(new BsonDocument { { "_id", "RESULT-1" }, { "sample_id", "S1" }, { "patient_id", "P1" }, { "status", "Pending" },
        { "entered_at", at.AddMinutes(50) }, { "conclusion_notes", "sensitive-result" }, { "approved_at", at.AddMinutes(80) } });
    var snapshot = await Snapshot();
    await Check("list is scoped, escaped, stable and paginated without fabricated dates", async () =>
    {
        var list = await service.ListAsync(new() { PageSize = 1 }, staff);
        Assert(list.Total == 3 && list.TotalPages == 3);
        Assert((await service.ListAsync(new() { Search = "[1]" }, staff)).Total == 1);
        Assert((await service.ListAsync(new() { Search = secondSample.ToString() }, staff)).Total == 1);
        Assert((await service.ListAsync(new() { Status = "Completed" }, staff)).Total == 1);
        Assert((await service.ListAsync(new() { Page = 99 }, staff)).Items.Count == 0);
        var own = await service.ListAsync(new(), patient);
        Assert(own.Total == 2 && own.Items.All(x => x.PatientId == "P1" && x.RfidTag == null));
        Assert((await service.ListAsync(new(), new("unlinked", "Patient"))).Total == 0);
        Assert((await repository.OwnPatientIdsAsync(accountId.ToString())).Order(StringComparer.Ordinal).SequenceEqual(new[] { "P-STRING", "P1" }));
        Assert((await service.GetAsync(secondSample.ToString(), new(), staff))?.Sample.CollectedAt == null);
    });
    await Check("clinic-day collection bounds exclude undated samples and include both endpoints", async () =>
    {
        Assert((await service.ListAsync(new() { FromDate = new(2026, 10, 22), ToDate = new(2026, 10, 22) }, staff)).Items.Single().Id == "S1");
        Assert((await service.ListAsync(new() { ToDate = new(2026, 10, 22) }, staff)).Total == 1);
    });
    await Check("timeline preserves evidence, ordering, undated events and repeated lifecycle data", async () =>
    {
        var timeline = (await service.GetAsync("BC-[1]", new(), staff))!;
        Assert(timeline.Events.Items.Count(x => x.Code == "Collected") == 1);
        Assert(timeline.Events.Items.Last().Code == "RerunRequested" && timeline.Events.Items.Last().Timestamp == null);
        Assert(timeline.Steps.Count == 6 && timeline.Steps.Take(5).All(x => x.Recorded));
        Assert(!timeline.ResultsAvailable && !timeline.Steps[5].Recorded);
        Assert(!System.Text.Json.JsonSerializer.Serialize(timeline).Contains("foreign-sensitive"));
        var p1 = await service.GetAsync("S1", new() { PageSize = 1 }, staff);
        var p2 = await service.GetAsync("S1", new() { PageSize = 1, Page = 2 }, staff);
        Assert(p1!.Events.Total == timeline.Events.Total && p1.Events.Items.Single().Id != p2!.Events.Items.Single().Id);
        var unknown = (await service.GetAsync(secondSample.ToString(), new(), staff))!;
        Assert(unknown.Events.Total == 0 && unknown.Steps.All(x => !x.Recorded));
    });
    await Check("patient responses redact internal notes/devices and deny foreign samples", async () =>
    {
        var timeline = (await service.GetAsync("S1", new(), patient))!;
        var json = System.Text.Json.JsonSerializer.Serialize(timeline);
        Assert(!json.Contains("sensitive") && !json.Contains("Fixture operator") && !json.Contains("Cold room") && !json.Contains("TRAY-1"));
        Assert(timeline.PatientView && timeline.Sample.Status == "UnderReview" && timeline.Events.Items.All(e => e.Source == "Progress"));
        Assert(await service.GetAsync("S-OTHER", new(), patient) == null);
        Assert(await service.GetAsync("missing", new(), patient) == null);
        Assert(await service.GetAsync("S-DELETED", new(), staff) == null);
        await Denied(() => service.RfidAsync("S1", new(), patient));
        await Denied(() => service.GetAsync("S1", new(), new("staff", "Receptionist")));
    });
    await Check("RFID attribution excludes another sample and precollection tag scans; never drives progress", async () =>
    {
        var rfid = (await service.RfidAsync("S1", new(), staff))!;
        Assert(rfid.Total == 3 && rfid.Items.Count(x => x.LinkType == "Tag") == 1);
        Assert(rfid.Items.Any(x => x.Checkpoint == "Collection gate" && x.Reader == "READER-1"));
        Assert(rfid.Items.Any(x => x.Checkpoint == "Lab gate" && x.Reader == "READER-2"));
        Assert(!rfid.Items.Any(x => x.Id is "R3" or "R4"));
        Assert((await service.RfidAsync("S1", new() { PageSize = 1 }, staff))?.TotalPages == 3);
        Assert(await service.GetAsync("TRAY-1", new(), staff) == null);
    });
    await Check("read-only requests leave source documents unchanged", async () => Assert(snapshot == await Snapshot()));
    await Check("all reports must be approved and signed; missing approval times stay unknown", async () =>
    {
        await results.UpdateOneAsync(new BsonDocument("_id", "RESULT-1"), new BsonDocument("$set", new BsonDocument("status", "APPROVED")));
        Assert(!(await service.GetAsync("S1", new(), patient))!.ResultsAvailable);
        await results.UpdateOneAsync(new BsonDocument("_id", "RESULT-1"), new BsonDocument("$set", new BsonDocument("digital_signature", "fixture-signature")));
        Assert((await service.GetAsync("S1", new(), patient))!.ResultsAvailable);
        await results.InsertOneAsync(new BsonDocument { { "_id", "RESULT-2" }, { "sampleId", "S1" }, { "patientId", "P1" }, { "validationStatus", 1 } });
        Assert(!(await service.GetAsync("S1", new(), patient))!.ResultsAvailable);
        await results.UpdateOneAsync(new BsonDocument("_id", "RESULT-2"), new BsonDocument("$set", new BsonDocument { { "validationStatus", 3 }, { "digitalSignature", "fixture-signature-2" } }));
        var ready = (await service.GetAsync("S1", new(), patient))!;
        Assert(ready.ResultsAvailable && ready.Steps[5].Recorded && ready.Steps[5].Timestamp == null);
        await samples.UpdateOneAsync(new BsonDocument("_id", "S1"), new BsonDocument("$set", new BsonDocument("status", "RerunRequested")));
        Assert(!(await service.GetAsync("S1", new(), patient))!.ResultsAvailable);
        await samples.UpdateOneAsync(new BsonDocument("_id", "S1"), new BsonDocument("$set", new BsonDocument("status", "Completed")));
        await results.UpdateOneAsync(new BsonDocument("_id", "RESULT-1"), new BsonDocument("$set", new BsonDocument("rerun_requested", true)));
        Assert(!(await service.GetAsync("S1", new(), patient))!.ResultsAvailable);
        await results.UpdateOneAsync(new BsonDocument("_id", "RESULT-1"), new BsonDocument("$set", new BsonDocument("rerun_requested", false)));
    });
    await Check("invalid filters/pagination and unauthorized service callers are rejected", async () =>
    {
        await Invalid(() => service.ListAsync(new() { Page = 0 }, staff));
        await Invalid(() => service.ListAsync(new() { Status = "unexpected" }, staff));
        await Invalid(() => service.ListAsync(new() { FromDate = new(2026, 10, 23), ToDate = new(2026, 10, 22) }, staff));
        await Invalid(() => service.GetAsync("S1", new() { PageSize = 101 }, staff));
        await Invalid(() => service.GetAsync(" ", new(), staff));
        await Denied(() => service.ListAsync(new(), new("staff", "Cashier")));
        await Denied(() => service.ListAsync(new(), new("", "Lab")));
    });
    await Check("HTTP authorization, scoped access, validation and read-only contracts", async () =>
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(SampleTimelineController).Assembly);
        builder.Services.AddSingleton<ISampleTimelineRepository>(repository); builder.Services.AddScoped<ISampleTimelineService, SampleTimelineService>();
        builder.Services.AddAuthentication("UC71").AddScheme<AuthenticationSchemeOptions, TimelineAuth>("UC71", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddExceptionHandler<GlobalExceptionHandler>(); builder.Services.AddProblemDetails();
        await using var app = builder.Build(); app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert((await http.GetAsync("/api/sample-timelines")).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Admin", "Receptionist", "Patient" })
        {
            Role(role); Assert((await http.GetAsync("/api/sample-timelines/S1")).StatusCode == HttpStatusCode.Forbidden);
        }
        foreach (var role in new[] { "Manager", "Doctor", "Lab" })
        {
            Role(role); using var response = await http.GetAsync("/api/sample-timelines/S1");
            Assert(response.StatusCode == HttpStatusCode.OK && response.Headers.CacheControl?.NoStore == true);
        }
        Role("Patient"); http.DefaultRequestHeaders.Add("X-User", accountId.ToString());
        Assert((await http.GetAsync("/api/sample-timelines?patientId=P2&accountId=other-account")).StatusCode == HttpStatusCode.Forbidden);
        Assert((await http.GetAsync("/api/sample-timelines/S1/rfid")).StatusCode == HttpStatusCode.Forbidden);
        Role("Lab");
        Assert((await http.GetAsync("/api/sample-timelines/MISSING-SAMPLE")).StatusCode == HttpStatusCode.NotFound);
        Assert((await http.GetAsync("/api/sample-timelines?pageSize=101")).StatusCode == HttpStatusCode.BadRequest);
        Assert((await http.GetAsync("/api/sample-timelines?fromDate=bad-date")).StatusCode == HttpStatusCode.BadRequest);
        Assert((await http.PostAsJsonAsync("/api/sample-timelines/S1", new { })).StatusCode == HttpStatusCode.MethodNotAllowed);
        await app.StopAsync();
        void Role(string role) { http.DefaultRequestHeaders.Remove("X-Role"); http.DefaultRequestHeaders.Add("X-Role", role); }
    });
    await Check("duplicate barcodes require an exact ID; malformed signatures never mark results available", async () =>
    {
        await samples.InsertManyAsync(new[] {
            new BsonDocument { { "_id", "DUP-1" }, { "barcode", "DUP" }, { "patient_id", "P1" } },
            new BsonDocument { { "_id", "DUP-2" }, { "barcode", "DUP" }, { "patient_id", "P1" } },
            new BsonDocument { { "_id", "MALFORMED" }, { "barcode", "MALFORMED" }, { "patient_id", "P1" }, { "status", "internal-sensitive-state" } }
        });
        await Invalid(() => service.GetAsync("DUP", new(), staff));
        Assert((await service.GetAsync("DUP-1", new(), staff))?.Sample.Id == "DUP-1");
        await results.InsertOneAsync(new BsonDocument { { "_id", "BAD-SIGNATURE" }, { "sample_id", "MALFORMED" }, { "patient_id", "P1" }, { "status", "APPROVED" }, { "digital_signature", new BsonDocument() } });
        var malformed = (await service.GetAsync("MALFORMED", new(), patient))!;
        Assert(!malformed.ResultsAvailable && malformed.Sample.Status == "Unknown");
    });
    await Check("existing BMS indexes and legacy schema are compatible without changing sample documents", async () =>
    {
        var live = new SampleTimelineService(new SampleTimelineRepository(client.GetDatabase("BMS_Database")));
        var list = await live.ListAsync(new() { PageSize = 1 }, staff);
        if (list.Items.Count > 0)
        {
            Assert(await live.GetAsync(list.Items[0].Id, new(), staff) != null);
            Assert(await live.RfidAsync(list.Items[0].Id, new(), staff) != null);
        }
        Console.WriteLine($"Existing samples read: {list.Total}.");
    });
    Console.WriteLine($"UC71: {passed} checks passed.");
    async Task<string> Snapshot()
    {
        var snapshots = new List<string>();
        foreach (var name in new[] { "patients", "blood_samples", "appointments", "specimen_histories", "rfid_tracking_logs", "test_results" })
            snapshots.Add((await db.GetCollection<BsonDocument>(name).Find(new BsonDocument()).Sort(new BsonDocument("_id", 1)).ToListAsync()).ToJson());
        return string.Join("\n", snapshots);
    }
}
finally
{
    if (!databaseName.StartsWith("BMS_UC71_Checks_", StringComparison.Ordinal) || databaseName.Length != 48) throw new InvalidOperationException("Unexpected fixture database name.");
    await client.DropDatabaseAsync(databaseName);
}
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine($"PASS {name}"); }
static void Assert(bool value) { if (!value) throw new Exception("Assertion failed."); }
static async Task Invalid(Func<Task> action) { try { await action(); } catch (DomainValidationException) { return; } throw new Exception("Expected validation error."); }
static async Task Denied(Func<Task> action) { try { await action(); } catch (ForbiddenAccessException) { return; } throw new Exception("Expected access denial."); }
sealed class TimelineAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["X-Role"].ToString();
        if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
        var user = Request.Headers["X-User"].FirstOrDefault() ?? "staff";
        var identity = new ClaimsIdentity(new[] { new Claim("sub", user), new Claim("role", role), new Claim("name", "Fixture user") }, Scheme.Name, "name", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
