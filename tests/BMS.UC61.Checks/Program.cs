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
    var service = new LabProfileService(repository);
    await Check("reads legacy ObjectId/string profiles without inventing timestamps", async () => {
        var legacy = await service.GetAsync(a.ToString());
        Assert(legacy?.Revision == 0 && legacy.CreatedAt == null && legacy.RoomName == "Test hematology room");
        Assert((await service.GetAsync(b.ToString()))?.FullName == "Test B");
        Assert(await service.GetAsync("archived") == null);
    });
    await Check("filters literal name/qualification/id and room with stable pagination", async () => {
        Assert((await service.ListAsync(new() { Search = "[a]" })).Total == 1);
        Assert((await service.ListAsync(new() { Search = "BIOCHEMISTRY" })).Total == 1);
        Assert((await service.ListAsync(new() { Search = a.ToString() })).Total == 1);
        Assert((await service.ListAsync(new() { RoomId = "ROOM-A" })).Total == 2);
        var first = await service.ListAsync(new() { PageSize = 1 });
        var second = await service.ListAsync(new() { PageSize = 1, Page = 2 });
        Assert(first.Total == 3 && first.Items.Single().Id != second.Items.Single().Id);
        Assert((await service.ListAsync(new() { Page = 99 })).Items.Count == 0);
    });
    await Check("creation-date range uses inclusive clinic days and excludes undated records", async () => {
        var result = await service.ListAsync(new() { CreatedFrom = new(2026,10,22), CreatedTo = new(2026,10,22) });
        Assert(result.Total == 1 && result.Items.Single().Id == b.ToString());
    });
    await Check("invalid filters are rejected in the service", async () => {
        await Reject(() => service.ListAsync(new() { Page = 0 }));
        await Reject(() => service.ListAsync(new() { PageSize = 101 }));
        await Reject(() => service.ListAsync(new() { CreatedFrom = new(2026,10,23), CreatedTo = new(2026,10,22) }));
    });
    await Check("HTTP read contracts enforce management authorization and validation", async () => {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(LabProfilesController).Assembly);
        builder.Services.AddSingleton<ILabProfileRepository>(repository); builder.Services.AddScoped<ILabProfileService, LabProfileService>();
        builder.Services.AddAuthentication("UC61").AddScheme<AuthenticationSchemeOptions, LabProfileAuth>("UC61", _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddExceptionHandler<GlobalExceptionHandler>(); builder.Services.AddProblemDetails();
        await using var app = builder.Build(); app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert((await http.GetAsync("/api/lab-profiles")).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Patient", "Doctor", "LabTech", "Receptionist", "WarehouseStaff" }) {
            http.DefaultRequestHeaders.Remove("X-Role"); http.DefaultRequestHeaders.Add("X-Role", role);
            Assert((await http.GetAsync("/api/lab-profiles")).StatusCode == HttpStatusCode.Forbidden);
        }
        foreach (var role in new[] { "Admin", "Manager", "LabHead" }) {
            http.DefaultRequestHeaders.Remove("X-Role"); http.DefaultRequestHeaders.Add("X-Role", role);
            Assert((await http.GetAsync("/api/lab-profiles")).StatusCode == HttpStatusCode.OK);
        }
        Assert((await http.GetFromJsonAsync<LabProfileListDto>("/api/lab-profiles?pageSize=1"))?.Total == 3);
        Assert((await http.GetAsync("/api/lab-profiles/rooms")).StatusCode == HttpStatusCode.OK);
        Assert((await http.GetAsync("/api/lab-profiles/missing")).StatusCode == HttpStatusCode.NotFound);
        Assert((await http.GetAsync("/api/lab-profiles?pageSize=101")).StatusCode == HttpStatusCode.BadRequest);
        Assert((await http.GetAsync("/api/lab-profiles?createdFrom=bad-date")).StatusCode == HttpStatusCode.BadRequest);
        await app.StopAsync();
    });
    await Check("existing BMS profile schema can be read without changing staff data", async () => {
        var live = new LabProfileService(new LabProfileRepository(client.GetDatabase("BMS_Database")));
        var result = await live.ListAsync(new());
        if (result.Items.Count > 0) Assert(await live.GetAsync(result.Items[0].Id) != null);
        Console.WriteLine($"Existing Lab profiles read: {result.Total}.");
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
sealed class LabProfileAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        var role = Request.Headers["X-Role"].ToString();
        if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "manager-check"), new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.Name, "Test manager") }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
