using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BMS.Api.Controllers.Inventory;
using BMS.Api.Middleware;
using BMS.Application.DTOs.Inventory;
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
using MongoDB.Driver;

// Only this disposable database is written; the real BMS inventory remains untouched.
var client = new MongoClient("mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=3000");
var databaseName = "BMS_UC56_Checks_" + Guid.NewGuid().ToString("N");
var database = client.GetDatabase(databaseName);
var today = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
var passed = 0;
try
{
    var repository = new InventoryRepository(database);
    await database.GetCollection<Reagent>("reagents").InsertManyAsync(new[] {
        Lot("expired", "REA-EXPIRED", today.AddDays(-1), 4, 2),
        Lot("today", "REA-TODAY", today, 0, 1),
        Lot("soon", "REA-SOON", today.AddDays(29), 5, 5),
        Lot("valid", "REA-VALID", today.AddDays(30), 10, 1),
        Lot("unknown", "REA-UNKNOWN", null, 8, 1),
        Lot("literal", "REA-[literal]", today.AddDays(30), 9, 1)
    });
    await database.GetCollection<InventoryTransaction>("inventory_transactions").InsertManyAsync(
        Enumerable.Range(1, 25).Select(n => new InventoryTransaction { Id = $"tx-{n:D2}", ReagentCode = "REA-TODAY",
            Type = "STOCK_IN", Quantity = 1, UnitPrice = 150000, Department = "Test warehouse", Timestamp = today.AddMinutes(n) }));
    var service = new InventoryService(repository, new ClinicClock());
    await Check("pages rows while counts cover the full filtered set", async () => {
        var result = await service.ListAsync(new() { PageSize = 2 });
        Assert(result.Total == 6 && result.TotalPages == 3 && result.Items.Count == 2);
        Assert(result.Summary.LowStock == 2 && result.Summary.ExpiringSoon == 2 && result.Summary.Expired == 1);
        Assert(result.AsOfDate == today.Date);
    });
    await Check("expiry boundaries use the clinic day and warn below 30 days", async () => {
        var result = await service.ListAsync(new());
        Assert(result.Items.Single(x => x.Id == "expired").ExpiryStatus == "EXPIRED");
        Assert(result.Items.Single(x => x.Id == "today").ExpiryStatus == "EXPIRING_SOON");
        Assert(result.Items.Single(x => x.Id == "soon").ExpiryStatus == "EXPIRING_SOON");
        Assert(result.Items.Single(x => x.Id == "valid").ExpiryStatus == "AVAILABLE");
        Assert(result.Items.Single(x => x.Id == "unknown").ExpiryStatus == "UNKNOWN_EXPIRY");
    });
    await Check("filters match server-derived warnings including stock equality", async () => {
        Assert((await service.ListAsync(new() { Status = "LOW_STOCK" })).Total == 2);
        Assert((await service.ListAsync(new() { Status = "EXPIRING_SOON" })).Total == 2);
        Assert((await service.ListAsync(new() { Status = "EXPIRED" })).Total == 1);
        Assert((await service.ListAsync(new() { Status = "AVAILABLE" })).Total == 2);
        Assert((await service.ListAsync(new() { Status = "UNKNOWN_EXPIRY" })).Total == 1);
    });
    await Check("search is literal, trimmed, and case insensitive across existing fields", async () => {
        Assert((await service.ListAsync(new() { Search = " [LITERAL] " })).Total == 1);
        Assert((await service.ListAsync(new() { Search = "Alpha" })).Total == 6);
        Assert((await service.ListAsync(new() { Search = "LOT-valid" })).Items.Single().Id == "valid");
        Assert((await service.ListAsync(new() { Search = "not found" })).Total == 0);
    });
    await Check("expiry date range is inclusive and excludes missing dates", async () => {
        var result = await service.ListAsync(new() { ExpiryFrom = today.AddDays(30), ExpiryTo = today.AddDays(30) });
        Assert(result.Total == 2 && result.Items.All(x => x.ExpiryStatus == "AVAILABLE"));
        Assert((await service.ListAsync(new() { ExpiryTo = today.AddDays(-1) })).Total == 1);
    });
    await Check("invalid filters are rejected at the application boundary", async () => {
        await Reject(() => service.ListAsync(new() { Page = 0 }));
        await Reject(() => service.ListAsync(new() { PageSize = 101 }));
        await Reject(() => service.ListAsync(new() { Search = new string('x', 101) }));
        await Reject(() => service.ListAsync(new() { Status = "invalid" }));
        await Reject(() => service.ListAsync(new() { ExpiryFrom = today, ExpiryTo = today.AddDays(-1) }));
    });
    await Check("stable pages and out-of-range pages do not change counts", async () => {
        var first = await service.ListAsync(new() { PageSize = 2 });
        var second = await service.ListAsync(new() { Page = 2, PageSize = 2 });
        Assert(!first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)).Any());
        var empty = await service.ListAsync(new() { Page = 99 });
        Assert(empty.Items.Count == 0 && empty.Total == 6);
    });
    await Check("detail includes only the latest 20 reagent-level movements", async () => {
        var detail = await service.GetAsync("today");
        Assert(detail?.TransactionCount == 25 && detail.Transactions.Count == 20);
        Assert(detail!.Transactions.First().Id == "tx-25");
        Assert((await service.GetAsync("unknown"))!.Transactions.Count == 0);
        Assert(await service.GetAsync("missing") == null);
    });
    await Check("HTTP authentication, role boundaries, aliases and read-only verbs", async () => {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(ReagentsController).Assembly);
        builder.Services.AddSingleton<IInventoryRepository>(repository);
        builder.Services.AddSingleton<TimeProvider>(new ClinicClock());
        builder.Services.AddScoped<IInventoryService, InventoryService>();
        builder.Services.AddAuthentication("InventoryChecks").AddScheme<AuthenticationSchemeOptions, InventoryAuth>("InventoryChecks", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>(); builder.Services.AddProblemDetails();
        await using var app = builder.Build();
        app.UseExceptionHandler(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert((await http.GetAsync("/api/inventory")).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Patient", "Doctor", "Receptionist", "Cashier", "Nurse" }) {
            http.DefaultRequestHeaders.Remove("X-Check-Role"); http.DefaultRequestHeaders.Add("X-Check-Role", role);
            Assert((await http.GetAsync("/api/inventory")).StatusCode == HttpStatusCode.Forbidden);
        }
        foreach (var role in new[] { "Admin", "Manager", "WarehouseStaff", "LabHead", "LabTech", "LabTechnician", "LaboratoryUser", "LabUser" }) {
            http.DefaultRequestHeaders.Remove("X-Check-Role"); http.DefaultRequestHeaders.Add("X-Check-Role", role);
            Assert((await http.GetAsync("/api/inventory")).StatusCode == HttpStatusCode.OK);
        }
        Assert((await http.GetFromJsonAsync<InventoryListDto>("/api/reagents?pageSize=2"))?.Total == 6);
        Assert((await http.GetAsync("/api/inventory?status=bad")).StatusCode == HttpStatusCode.BadRequest);
        Assert((await http.GetAsync("/api/inventory?pageSize=101")).StatusCode == HttpStatusCode.BadRequest);
        Assert((await http.GetAsync("/api/inventory?expiryFrom=bad-date")).StatusCode == HttpStatusCode.BadRequest);
        Assert((await http.GetAsync("/api/inventory/missing")).StatusCode == HttpStatusCode.NotFound);
        Assert((await http.GetAsync("/api/inventory/today")).StatusCode == HttpStatusCode.OK);
        Assert((await http.PostAsJsonAsync("/api/inventory", new { })).StatusCode == HttpStatusCode.Forbidden);
        http.DefaultRequestHeaders.Remove("X-Check-Role"); http.DefaultRequestHeaders.Add("X-Check-Role", "WarehouseStaff");
        Assert((await http.PostAsJsonAsync("/api/inventory", new { })).StatusCode == HttpStatusCode.BadRequest);
        var create = NewLot("HTTP-CREATE");
        var createdResponse = await http.PostAsJsonAsync("/api/inventory", create);
        Assert(createdResponse.StatusCode == HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        Assert(created != null && (await http.GetAsync(createdResponse.Headers.Location)).StatusCode == HttpStatusCode.OK);
        Assert((await http.PostAsJsonAsync("/api/inventory", create)).StatusCode == HttpStatusCode.Conflict);
        create.Quantity = -1;
        Assert((await http.PostAsJsonAsync("/api/inventory", create)).StatusCode == HttpStatusCode.BadRequest);
        await app.StopAsync();
    });
    await Check("existing BMS schema can be read without modifying stock", async () => {
        var liveDb = client.GetDatabase("BMS_Database");
        var live = new InventoryRepository(liveDb);
        var liveService = new InventoryService(live, TimeProvider.System);
        var all = await liveService.ListAsync(new());
        if (all.Items.Count > 0) {
            var detail = await liveService.GetAsync(all.Items[0].Id);
            Assert(detail?.Item.Id == all.Items[0].Id);
        }
        Console.WriteLine($"Existing inventory: {all.Total} lots read successfully.");
    });
    await Check("create validates values and stores opening balance with audit atomically", async () => {
        var request = NewLot("SERVICE-CREATE");
        var created = await service.CreateAsync(request, "warehouse-1", "Warehouse One");
        var stored = await repository.GetAsync(created.Id);
        Assert(stored!.Changes.Single().ActorId == "warehouse-1" && stored.Changes.Single().After!.Quantity == request.Quantity);
        Assert(stored.Changes.Single().Before == null && created.Revision == 0);
        var bad = NewLot("BAD"); bad.Quantity = -1;
        await Reject(() => service.CreateAsync(bad, "warehouse-1", "Warehouse One"));
        bad = NewLot("BAD"); bad.Reason = "  ";
        await Reject(() => service.CreateAsync(bad, "warehouse-1", "Warehouse One"));
        bad = NewLot("BAD"); bad.Quantity = 0.0001m;
        await Reject(() => service.CreateAsync(bad, "warehouse-1", "Warehouse One"));
    });
    await Check("concurrent create prevents normalized code and lot duplicates", async () => {
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ => {
            try { await service.CreateAsync(NewLot("CONCURRENT"), "warehouse-1", "Warehouse One"); return true; }
            catch (InventoryConflictException) { return false; }
        }));
        Assert(results.Count(x => x) == 1);
        var request = NewLot("CONCURRENT"); request.Code = request.Code.ToLowerInvariant();
        try { await service.CreateAsync(request, "warehouse-1", "Warehouse One"); throw new Exception("Expected duplicate conflict."); }
        catch (InventoryConflictException) { }
    });
    Console.WriteLine($"UC56: {passed} checks passed.");
}
finally
{
    // The exact generated name is constrained to this run; never drop BMS_Database.
    if (!databaseName.StartsWith("BMS_UC56_Checks_", StringComparison.Ordinal) || databaseName.Length != 48)
        throw new InvalidOperationException("Unexpected test database name.");
    await client.DropDatabaseAsync(databaseName);
}

Reagent Lot(string id, string code, DateTime? expiry, decimal qty, decimal min) => new() {
    Id = id, Code = code, Name = "Hóa chất " + id, Manufacturer = "Alpha", LotNumber = "LOT-" + id,
    ExpiryDate = expiry, Quantity = qty, MinimumStock = min, Unit = "Hộp"
};
CreateInventoryDto NewLot(string code) => new() { Code = code, LotNumber = "LOT-NEW", Unit = "Hộp", Name = "Test reagent",
    Manufacturer = "Test manufacturer", Quantity = 10, MinimumStock = 2, ExpiryDate = today.AddDays(90), Reason = "Initial stock check" };
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine($"PASS {name}"); }
static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
static async Task Reject(Func<Task> action) { try { await action(); } catch (DomainValidationException) { return; } throw new Exception("Expected validation failure."); }
sealed class ClinicClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 21, 17, 30, 0, TimeSpan.Zero); }
sealed class InventoryAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
        var role = Request.Headers["X-Check-Role"].ToString();
        if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "inventory-check-user"), new Claim(ClaimTypes.Role, role) }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
