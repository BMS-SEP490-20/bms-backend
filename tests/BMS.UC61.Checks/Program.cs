using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BMS.Api.Controllers.Laboratory;
using BMS.Api.Middleware;
using BMS.Application.DTOs.LabProfiles;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Exceptions;
using BMS.Core.Entities;
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
var databaseName = "BMS_UC61_Checks_" + Guid.NewGuid().ToString("N");
var database = client.GetDatabase(databaseName);
var a = ObjectId.GenerateNewId(); var b = ObjectId.GenerateNewId();
var passed = 0;
try
{
    var repository = new LabProfileRepository(database);
    var profiles = database.GetCollection<BsonDocument>("lab_user_profiles");
    await database.GetCollection<BsonDocument>("lab_rooms").InsertManyAsync(new[] {
        new BsonDocument { { "_id", "ROOM-A" }, { "room_code", "A" }, { "room_name", "Test hematology room" } },
        new BsonDocument { { "_id", "ROOM-B" }, { "room_code", "B" }, { "room_name", "Test biochemistry room" } }
    });
    await profiles.InsertManyAsync(new[] {
        new BsonDocument { { "_id", a }, { "full_name", "Test [A]" }, { "qualification", "Lab Alpha" }, { "assigned_room", "ROOM-A" } },
        new BsonDocument { { "_id", b.ToString() }, { "full_name", "Test B" }, { "qualification", "Biochemistry" }, { "assigned_room", "ROOM-B" }, { "created_at", new DateTime(2026,10,21,18,0,0,DateTimeKind.Utc) } },
        new BsonDocument { { "_id", "staff-c" }, { "full_name", "Test C" }, { "qualification", "Hematology" }, { "assigned_room", "ROOM-A" }, { "created_at", new DateTime(2026,10,23,0,0,0,DateTimeKind.Utc) } },
        new BsonDocument { { "_id", "archived" }, { "full_name", "Archived" }, { "is_deleted", true } }
    });
    var service = new LabProfileService(repository, TimeProvider.System);
    await Check("reads legacy ObjectId/string profiles without inventing timestamps", async () =>
    {
        var legacy = await service.GetAsync(a.ToString());
        Assert(legacy?.Revision == 0 && legacy.CreatedAt == null && legacy.RoomName == "Test hematology room");
        Assert((await service.GetAsync(b.ToString()))?.FullName == "Test B");
        Assert(await service.GetAsync("archived") == null);
    });
    await Check("filters literal name/qualification/id and room with stable pagination", async () =>
    {
        Assert((await service.ListAsync(new() { Search = "[a]" })).Total == 1);
        Assert((await service.ListAsync(new() { Search = "BIOCHEMISTRY" })).Total == 1);
        Assert((await service.ListAsync(new() { Search = a.ToString() })).Total == 1);
        Assert((await service.ListAsync(new() { RoomId = "ROOM-A" })).Total == 2);
        var first = await service.ListAsync(new() { PageSize = 1 });
        var second = await service.ListAsync(new() { PageSize = 1, Page = 2 });
        Assert(first.Total == 3 && first.Items.Single().Id != second.Items.Single().Id);
        Assert((await service.ListAsync(new() { Page = 99 })).Items.Count == 0);
    });
    await Check("creation-date range uses inclusive clinic days and excludes undated records", async () =>
    {
        var result = await service.ListAsync(new() { CreatedFrom = new(2026, 10, 22), CreatedTo = new(2026, 10, 22) });
        Assert(result.Total == 1 && result.Items.Single().Id == b.ToString());
        Assert((await service.ListAsync(new() { CreatedTo = new(2026, 10, 22) })).Total == 1);
    });
    await Check("invalid filters are rejected in the service", async () =>
    {
        await Reject(() => service.ListAsync(new() { Page = 0 }));
        await Reject(() => service.ListAsync(new() { PageSize = 101 }));
        await Reject(() => service.ListAsync(new() { CreatedFrom = new(2026, 10, 23), CreatedTo = new(2026, 10, 22) }));
    });
    await Check("HTTP read contracts enforce management authorization and validation", async () =>
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(LabProfilesController).Assembly);
        builder.Services.AddSingleton<ILabProfileRepository>(repository); builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddScoped<ILabProfileService, LabProfileService>();
        builder.Services.AddAuthentication("UC61").AddScheme<AuthenticationSchemeOptions, LabProfileAuth>("UC61", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddExceptionHandler<GlobalExceptionHandler>(); builder.Services.AddProblemDetails();
        await using var app = builder.Build(); app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert((await http.GetAsync("/api/lab-profiles")).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Patient", "Doctor", "LabTech", "Receptionist", "WarehouseStaff" })
        {
            http.DefaultRequestHeaders.Remove("X-Role"); http.DefaultRequestHeaders.Add("X-Role", role);
            Assert((await http.GetAsync("/api/lab-profiles")).StatusCode == HttpStatusCode.Forbidden);
            Assert((await http.PostAsJsonAsync("/api/lab-profiles", new { })).StatusCode == HttpStatusCode.Forbidden);
            Assert((await http.PutAsJsonAsync("/api/lab-profiles/test", new { })).StatusCode == HttpStatusCode.Forbidden);
            using var denied = new HttpRequestMessage(HttpMethod.Delete, "/api/lab-profiles/test") { Content = JsonContent.Create(new { }) };
            Assert((await http.SendAsync(denied)).StatusCode == HttpStatusCode.Forbidden);
        }
        foreach (var role in new[] { "Admin", "Manager", "LabHead" })
        {
            http.DefaultRequestHeaders.Remove("X-Role"); http.DefaultRequestHeaders.Add("X-Role", role);
            Assert((await http.GetAsync("/api/lab-profiles")).StatusCode == HttpStatusCode.OK);
        }
        Assert((await http.GetFromJsonAsync<LabProfileListDto>("/api/lab-profiles?pageSize=1"))?.Total == 3);
        Assert((await http.GetAsync("/api/lab-profiles/rooms")).StatusCode == HttpStatusCode.OK);
        Assert((await http.GetAsync("/api/lab-profiles/missing")).StatusCode == HttpStatusCode.NotFound);
        Assert((await http.GetAsync("/api/lab-profiles?pageSize=101")).StatusCode == HttpStatusCode.BadRequest);
        Assert((await http.GetAsync("/api/lab-profiles?createdFrom=bad-date")).StatusCode == HttpStatusCode.BadRequest);
        await database.GetCollection<BsonDocument>("accounts").InsertOneAsync(new BsonDocument { { "_id", "http-lab" }, { "FullName", "HTTP fixture" }, { "Username", "httpfixture" }, { "RoleId", "3" }, { "Status", "Active" } });
        var input = new CreateLabProfileDto { AccountId = "http-lab", FullName = "HTTP professional", Qualification = "Lab", AssignedRoom = "ROOM-A", Reason = "Create via HTTP" };
        Assert((await http.PostAsJsonAsync("/api/lab-profiles", input)).StatusCode == HttpStatusCode.Created);
        Assert((await http.PostAsJsonAsync("/api/lab-profiles", input)).StatusCode == HttpStatusCode.Conflict);
        Assert((await http.PostAsJsonAsync("/api/lab-profiles", new { fullName = "missing fields" })).StatusCode == HttpStatusCode.BadRequest);
        var edit = new UpdateLabProfileDto { FullName = "HTTP updated", Qualification = "Lab senior", AssignedRoom = "ROOM-B", Reason = "Update via HTTP", ExpectedRevision = 0 };
        Assert((await http.PutAsJsonAsync("/api/lab-profiles/http-lab", edit)).StatusCode == HttpStatusCode.OK);
        Assert((await http.PutAsJsonAsync("/api/lab-profiles/http-lab", edit)).StatusCode == HttpStatusCode.Conflict);
        edit.ExpectedRevision = null;
        Assert((await http.PutAsJsonAsync("/api/lab-profiles/http-lab", edit)).StatusCode == HttpStatusCode.BadRequest);
        http.DefaultRequestHeaders.Remove("X-Role"); http.DefaultRequestHeaders.Add("X-Role", "LabTechnician");
        Assert((await http.PostAsJsonAsync("/api/lab-profiles", input)).StatusCode == HttpStatusCode.Forbidden);
        Assert((await http.PutAsJsonAsync("/api/lab-profiles/http-lab", edit)).StatusCode == HttpStatusCode.Forbidden);
        http.DefaultRequestHeaders.Remove("X-Role"); http.DefaultRequestHeaders.Add("X-Role", "LabHead");
        async Task<HttpStatusCode> Remove(object body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, "/api/lab-profiles/http-lab") { Content = JsonContent.Create(body) };
            using var response = await http.SendAsync(request); return response.StatusCode;
        }
        Assert(await Remove(new { reason = "Missing revision" }) == HttpStatusCode.BadRequest);
        Assert(await Remove(new { expectedRevision = 0, reason = "Stale revision" }) == HttpStatusCode.Conflict);
        Assert(await Remove(new { expectedRevision = 1, reason = "Archive via HTTP" }) == HttpStatusCode.NoContent);
        Assert((await http.GetAsync("/api/lab-profiles/http-lab")).StatusCode == HttpStatusCode.NotFound);
        Assert(await Remove(new { expectedRevision = 2, reason = "Already archived" }) == HttpStatusCode.NotFound);
        await app.StopAsync();
    });
    await Check("existing BMS profile schema can be read without changing staff data", async () =>
    {
        var live = new LabProfileService(new LabProfileRepository(client.GetDatabase("BMS_Database")), TimeProvider.System);
        var result = await live.ListAsync(new());
        if (result.Items.Count > 0) Assert(await live.GetAsync(result.Items[0].Id) != null);
        Console.WriteLine($"Existing Lab profiles read: {result.Total}.");
    });
    var accounts = database.GetCollection<BsonDocument>("accounts");
    var newId = ObjectId.GenerateNewId().ToString();
    foreach (var (id, role, status) in new[] { (newId, "3", "Active"), (a.ToString(), "4", "Active"), (b.ToString(), "3", "Active"),
        ("not-lab", "2", "Active"), ("inactive-lab", "3", "Inactive"), ("archived", "4", "Active"), ("race-lab", "4", "Active") })
        await accounts.InsertOneAsync(new BsonDocument { { "_id", ObjectId.TryParse(id, out var oid) ? oid : id }, { "RoleId", role },
            { "Status", status }, { "FullName", "Fixture " + id }, { "Username", "fixture" }, { "Password", "fixture-secret-never-return" } });
    var actor = new LabProfileActor("manager-check", "Test manager");
    CreateLabProfileDto Create(string id) => new() { AccountId = id, FullName = "New professional", Qualification = "Lab specialist", AssignedRoom = "ROOM-A", Reason = "Create professional record" };
    await Check("safe paged account options include reserved profiles and exclude inactive/non-Lab staff", async () =>
    {
        var options = await service.AccountsAsync(null, 1, 100);
        Assert(options.Total == 6 && options.Items.Any(x => x.Id == a.ToString() && x.HasProfile));
        Assert(options.Items.Any(x => x.Id == b.ToString() && x.HasProfile));
        Assert(options.Items.Any(x => x.Id == "archived" && x.HasProfile));
        Assert(!System.Text.Json.JsonSerializer.Serialize(options).Contains("fixture-secret"));
        Assert((await service.AccountsAsync(newId, 1, 1)).Total == 1);
    });
    await Check("creation validates account/room/details and preserves existing accounts", async () =>
    {
        await Reject(() => service.CreateAsync(Create("not-lab"), actor));
        await Reject(() => service.CreateAsync(Create("inactive-lab"), actor));
        var invalid = Create(newId); invalid.AssignedRoom = "missing"; await Reject(() => service.CreateAsync(invalid, actor));
        invalid = Create(newId); invalid.Reason = "   x  "; await Reject(() => service.CreateAsync(invalid, actor));
        await Conflict(() => service.CreateAsync(Create(a.ToString()), actor));
        await Conflict(() => service.CreateAsync(Create(b.ToString()), actor));
        await Conflict(() => service.CreateAsync(Create("archived"), actor));
        var before = await accounts.Find(new BsonDocument("_id", ObjectId.Parse(newId))).FirstAsync();
        var created = await service.CreateAsync(Create(newId), actor);
        var raw = await profiles.Find(new BsonDocument("_id", ObjectId.Parse(newId))).FirstAsync();
        Assert(created.Id == newId && created.Revision == 0 && created.CreatedAt.HasValue && raw["audit"].AsBsonArray.Count == 1);
        Assert(raw["audit"][0]["ActorId"] == actor.Id);
        Assert(before.Equals(await accounts.Find(new BsonDocument("_id", ObjectId.Parse(newId))).FirstAsync()));
    });
    await Check("concurrent creates reserve one account identity", async () =>
    {
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await service.CreateAsync(Create("race-lab"), actor); return true; } catch (LabProfileConflictException) { return false; }
        }));
        Assert(attempts.Count(x => x) == 1);
        Assert(await profiles.CountDocumentsAsync(new BsonDocument("_id", "race-lab")) == 1);
    });
    UpdateLabProfileDto Edit(long? revision = 0) => new() { FullName = "Updated professional", Qualification = "Senior specialist", AssignedRoom = "ROOM-B", Reason = "Correct professional details", ExpectedRevision = revision };
    await Check("legacy update preserves identity, unknown fields, timestamps and account", async () =>
    {
        await profiles.UpdateOneAsync(new BsonDocument("_id", a), new BsonDocument("$set", new BsonDocument("legacy_extra", "preserved")));
        var before = await accounts.Find(new BsonDocument("_id", a)).FirstAsync();
        var result = await service.UpdateAsync(a.ToString(), Edit(), actor);
        var raw = await profiles.Find(new BsonDocument("_id", a)).FirstAsync();
        Assert(result.Id == a.ToString() && result.Revision == 1 && result.CreatedAt == null && result.AssignedRoom == "ROOM-B");
        Assert(raw["legacy_extra"] == "preserved" && raw["audit"][0]["Before"]["FullName"] == "Test [A]");
        Assert(before.Equals(await accounts.Find(new BsonDocument("_id", a)).FirstAsync()));
        await Conflict(() => service.UpdateAsync(a.ToString(), Edit(), actor));
        await Reject(() => service.UpdateAsync(a.ToString(), Edit(null), actor));
        await Reject(() => service.UpdateAsync(a.ToString(), Edit(-1), actor));
        var invalid = Edit(1); invalid.AssignedRoom = "missing"; await Reject(() => service.UpdateAsync(a.ToString(), invalid, actor));
    });
    await Check("only one concurrent update wins; audit appends atomically", async () =>
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await service.UpdateAsync(newId, Edit(), actor); return true; } catch (LabProfileConflictException) { return false; }
        }));
        Assert(results.Count(x => x) == 1);
        var raw = await profiles.Find(new BsonDocument("_id", ObjectId.Parse(newId))).FirstAsync();
        Assert(raw["revision"].ToInt64() == 1 && raw["audit"].AsBsonArray.Count == 2);
    });
    await Check("profile cannot be edited after linked account deactivation", async () =>
    {
        await accounts.UpdateOneAsync(new BsonDocument("_id", b), new BsonDocument("$set", new BsonDocument("Status", "Inactive")));
        await Reject(() => service.UpdateAsync(b.ToString(), Edit(), actor));
        Assert((await service.GetAsync(b.ToString()))?.Revision == 0);
    });
    var assignments = database.GetCollection<BsonDocument>("lab_assignments");
    var today = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7).Date.AddHours(-7), DateTimeKind.Utc);
    DeleteLabProfileDto Delete(long? revision) => new() { ExpectedRevision = revision, Reason = "Archive professional profile" };
    await Check("current/future or undated active assignments block deletion without writes", async () =>
    {
        await assignments.InsertOneAsync(new BsonDocument { { "_id", "today" }, { "lab_user_id", a.ToString() }, { "shift_date", today }, { "status", "Active" } });
        await Conflict(() => service.DeleteAsync(a.ToString(), Delete(1), actor));
        Assert((await service.GetAsync(a.ToString()))?.Revision == 1);
        await assignments.DeleteOneAsync(new BsonDocument("_id", "today"));
        await assignments.InsertOneAsync(new BsonDocument { { "_id", "future" }, { "lab_user_id", a }, { "shift_date", today.AddDays(1) }, { "status", "Scheduled" } });
        await Conflict(() => service.DeleteAsync(a.ToString(), Delete(1), actor));
        await assignments.DeleteOneAsync(new BsonDocument("_id", "future"));
        await assignments.InsertOneAsync(new BsonDocument { { "_id", "unknown-date" }, { "lab_user_id", a }, { "status", "Active" } });
        await Conflict(() => service.DeleteAsync(a.ToString(), Delete(1), actor));
        await assignments.DeleteOneAsync(new BsonDocument("_id", "unknown-date"));
        await Reject(() => service.DeleteAsync(a.ToString(), Delete(null), actor));
        var bad = Delete(1); bad.Reason = " x "; await Reject(() => service.DeleteAsync(a.ToString(), bad, actor));
    });
    await Check("soft deletion preserves account, assignments and audit; identity stays reserved", async () =>
    {
        await assignments.InsertManyAsync(new[] {
            new BsonDocument { { "_id", "past" }, { "lab_user_id", a }, { "shift_date", today.AddDays(-1) }, { "status", "Active" } },
            new BsonDocument { { "_id", "cancelled" }, { "lab_user_id", a }, { "shift_date", today.AddDays(1) }, { "status", "Cancelled" } }
        });
        var accountBefore = await accounts.Find(new BsonDocument("_id", a)).FirstAsync();
        var assignmentsBefore = (await assignments.Find(new BsonDocument()).Sort(new BsonDocument("_id", 1)).ToListAsync()).ToJson();
        await Conflict(() => service.DeleteAsync(a.ToString(), Delete(0), actor));
        await service.DeleteAsync(a.ToString(), Delete(1), actor);
        var raw = await profiles.Find(new BsonDocument("_id", a)).FirstAsync();
        Assert(raw["is_deleted"].AsBoolean && raw["revision"].ToInt64() == 2 && raw["deleted_at"].IsValidDateTime);
        Assert(raw["audit"].AsBsonArray.Count == 2 && raw["audit"][1]["Action"] == "Delete" && raw["audit"][1]["After"].IsBsonNull);
        Assert(await service.GetAsync(a.ToString()) == null && (await service.ListAsync(new() { Search = a.ToString() })).Total == 0);
        Assert(accountBefore.Equals(await accounts.Find(new BsonDocument("_id", a)).FirstAsync()));
        Assert(assignmentsBefore == (await assignments.Find(new BsonDocument()).Sort(new BsonDocument("_id", 1)).ToListAsync()).ToJson());
        await Conflict(() => service.CreateAsync(Create(a.ToString()), actor));
        try { await service.UpdateAsync(a.ToString(), Edit(2), actor); throw new Exception("Resurrected deleted profile"); } catch (LabProfileNotFoundException) { }
        Assert(await repository.UpdateAsync(a.ToString(), 2, new("No", "No", "ROOM-A"), new("Update", actor.Id, actor.Name, DateTime.UtcNow, "No resurrection", null, null)) == null);
    });
    await Check("concurrent delete/update cannot both apply a stale version", async () =>
    {
        var attempts = await Task.WhenAll(new[] { Task.Run(async () => {
            try { await service.DeleteAsync(newId, Delete(1), actor); return true; } catch (LabProfileConflictException) { return false; }
        }), Task.Run(async () => {
            try { await service.UpdateAsync(newId, Edit(1), actor); return true; } catch (LabProfileConflictException) { return false; } catch (LabProfileNotFoundException) { return false; }
        }) });
        Assert(attempts.Count(x => x) == 1);
        var raw = await profiles.Find(new BsonDocument("_id", ObjectId.Parse(newId))).FirstAsync();
        Assert(raw["revision"].ToInt64() == 2 && raw["audit"].AsBsonArray.Count == 3);
    });
    Console.WriteLine($"UC61: {passed} checks passed.");
}
finally
{
    if (!databaseName.StartsWith("BMS_UC61_Checks_", StringComparison.Ordinal) || databaseName.Length != 48) throw new InvalidOperationException("Unexpected fixture database name.");
    await client.DropDatabaseAsync(databaseName);
}
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine($"PASS {name}"); }
static void Assert(bool value) { if (!value) throw new Exception("Assertion failed."); }
static async Task Reject(Func<Task> action) { try { await action(); } catch (DomainValidationException) { return; } throw new Exception("Expected validation error."); }
static async Task Conflict(Func<Task> action) { try { await action(); } catch (LabProfileConflictException) { return; } throw new Exception("Expected conflict."); }
sealed class LabProfileAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["X-Role"].ToString();
        if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim("sub", "manager-check"), new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.Name, "Test manager") }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
