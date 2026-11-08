using System.Text.Json;
using BMS.Core.Entities;
using BMS.Core.Interfaces;
using BMS.Application.Services;
using BMS.Application.DTOs.TestResults;
using BMS.Core.Exceptions;
using BMS.Infrastructure.Repositories;
using BMS.Infrastructure.Mongo;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;
using BMS.Api.Controllers.Laboratory;
using Microsoft.AspNetCore.Mvc;

MongoMappings.Register();
var finds = 0;
var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("BMS_CHECK_MONGO") ?? "mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=3000");
settings.ClusterConfigurator = b => b.Subscribe<CommandStartedEvent>(e =>
{
    if (e.CommandName == "find" && e.Command["find"] == "test_parameters") Interlocked.Increment(ref finds);
});
var client = new MongoClient(settings);
var dbName = "BMS_LegacyResultSafety_Checks_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(dbName);
var results = db.GetCollection<BsonDocument>("test_results");
var parameters = db.GetCollection<BsonDocument>("test_parameters");
var repo = new TestResultRepository(db);
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}
try
{
    var canonical = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    var legacy = canonical.AddDays(-20);
    foreach (var canonicalFirst in new[] { true, false })
    {
        var doc = new BsonDocument("_id", "dates");
        if (canonicalFirst) doc.Add("createdAt", canonical);
        doc.Add("created_at", legacy);
        if (!canonicalFirst) doc.Add("createdAt", canonical);
        Check(BsonSerializer.Deserialize<TestResult>(doc).CreatedAt == canonical, "Canonical date must win in either BSON field order");
    }
    Check(BsonSerializer.Deserialize<TestResult>(new BsonDocument { { "_id", "legacy-date" }, { "created_at", legacy } }).CreatedAt == legacy, "Legacy date fallback");
    Check(BsonSerializer.Deserialize<TestResult>(new BsonDocument("_id", "missing-date")).CreatedAt == default, "Do not invent missing historical dates");
    var raw = BsonSerializer.Deserialize<TestResult>(new BsonDocument { { "_id", "json" }, { "backup_private_field", "private-sentinel" } });
    var json = JsonSerializer.Serialize(raw);
    Check(!json.Contains("private-sentinel") && !json.Contains("LegacyFields"), "Raw backup fields must not leak into JSON");
    Check(TestResultRules.IsSigned(BsonSerializer.Deserialize<TestResult>(new BsonDocument
        { { "_id", "mixed-signature" }, { "digitalSignature", "  " }, { "digital_signature", "signed" } })),
        "Blank canonical signature must not hide signed legacy metadata");

    var flags = new Dictionary<string, ResultFlag>
    {
        ["N"] = ResultFlag.Normal,
        ["NORMAL"] = ResultFlag.Normal,
        ["0"] = ResultFlag.Normal,
        ["H"] = ResultFlag.High,
        ["HIGH"] = ResultFlag.High,
        ["2"] = ResultFlag.High,
        ["L"] = ResultFlag.Low,
        ["LOW"] = ResultFlag.Low,
        ["1"] = ResultFlag.Low,
        ["HH"] = ResultFlag.CriticalPanic,
        ["LL"] = ResultFlag.CriticalPanic,
        ["CRITICAL_HIGH"] = ResultFlag.CriticalPanic,
        ["CRITICAL_LOW"] = ResultFlag.CriticalPanic,
        ["C"] = ResultFlag.CriticalPanic,
        ["CRITICALPANIC"] = ResultFlag.CriticalPanic,
        [" critical-high "] = ResultFlag.CriticalPanic,
        ["PANIC"] = ResultFlag.CriticalPanic,
        ["3"] = ResultFlag.CriticalPanic,
        ["unexpected-Code"] = ResultFlag.Unknown,
        [""] = ResultFlag.Unknown
    };
    for (var i = 0; i < 50; i++)
    {
        var flag = flags.ElementAt(i % flags.Count);
        await results.InsertOneAsync(new BsonDocument { { "_id", $"page-{i}" }, { "appointment_id", "page-order" }, { "created_at", legacy } });
        await parameters.InsertOneAsync(new BsonDocument { { "result_id", $"page-{i}" }, { "flag", flag.Key }, { "value_numeric", i } });
    }
    finds = 0;
    var page = await repo.GetPagedAsync(1, 50);
    Check(page.Items.Count == 50 && finds == 1, "50 legacy results must use exactly one parameter query");
    foreach (var item in page.Items)
    {
        var i = int.Parse(item.Id[5..]);
        var expected = flags.ElementAt(i % flags.Count);
        Check(item.Items.Single().Flag == expected.Value && item.Items.Single().OriginalFlag == expected.Key, "Preserve and classify legacy interpretation");
        Check(item.IsPanic == (expected.Value == ResultFlag.CriticalPanic), "Critical legacy interpretation must set panic");
    }
    finds = 0;
    Check((await repo.GetByAppointmentIdAsync("page-order")).Count == 50 && finds == 1, "Appointment results must hydrate in one batch too");
    var service = new TestResultService(repo, null!, null!, NullLogger<TestResultService>.Instance);
    var criticalDto = (await service.GetByIdAsync("page-9"))!;
    Check(criticalDto.IsPanic && criticalDto.Parameters.Single().Flag == "CriticalPanic" && criticalDto.Parameters.Single().OriginalFlag == "HH", "API DTO preserves critical interpretation");
    var unknownDto = (await service.GetByIdAsync("page-18"))!;
    Check(unknownDto.Parameters.Single().Flag == "Unknown" && unknownDto.Parameters.Single().OriginalFlag == "unexpected-Code", "API DTO exposes unknown interpretation");
    Check(!JsonSerializer.Serialize(criticalDto).Contains("LegacyFields"), "Read DTO excludes BSON extras");
    await results.UpdateOneAsync(new BsonDocument("_id", "page-9"), Builders<BsonDocument>.Update.Set("backup_private_field", "private-sentinel"));
    var controller = new TestResultController(service);
    foreach (var response in new[]
    {
        await controller.GetById("page-9", default),
        await controller.GetAll(1, 50),
        await controller.GetByAppointmentId("page-order", default)
    })
    {
        Check(response is OkObjectResult, "Result read controller succeeds");
        var body = JsonSerializer.Serialize(((OkObjectResult)response).Value);
        Check(!body.Contains("private-sentinel") && !body.Contains("LegacyFields"), "Controller read payload excludes raw backup fields");
        Check(body.Contains("CriticalPanic"), "Controller preserves critical interpretation");
    }
    var lookupController = new TestResultsController(null!, db);
    var lookupResponse = await lookupController.GetResultById("page-9");
    Check(lookupResponse is OkObjectResult, "Biochemical result lookup succeeds");
    var lookupBody = JsonSerializer.Serialize(((OkObjectResult)lookupResponse).Value);
    Check(lookupBody.Contains("HH") && !lookupBody.Contains("private-sentinel"), "Biochemical lookup preserves raw flag and excludes backup extras");
    await results.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
    await repo.AddAsync(new TestResult
    {
        Id = "canonical-items",
        CreatedAt = canonical,
        Items = [new TestResultItem { Flag = ResultFlag.High }]
    });
    finds = 0;
    Check((await repo.GetPagedAsync(1, 50)).Items.Single().Items.Single().Flag == ResultFlag.High && finds == 0,
        "Canonical embedded measurements must not query legacy parameters");
    await results.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
    var cases = new (string Field, BsonValue Value, bool Signed)[]
    {
        ("digitalSignature", "sig", true), ("digital_signature", "sig", true),
        ("status", "APPROVED", true), ("status", "approved", true),
        ("approvedAt", canonical, true), ("approved_at", canonical, true),
        ("validationStatus", 3, true), ("digitalSignature", "", false),
        ("digital_signature", "  ", false), ("digitalSignature", " \t", false),
        ("status", "Entered", false)
    };
    foreach (var legacyLinks in new[] { false, true })
        foreach (var test in cases)
        {
            var id = Guid.NewGuid().ToString();
            var sample = "sample-" + id;
            var order = "order-" + id;
            await results.InsertOneAsync(new BsonDocument { { "_id", id }, { legacyLinks ? "sample_id" : "sampleId", sample },
            { legacyLinks ? "appointment_id" : "appointmentId", order }, { test.Field, test.Value } });
            var result = (await repo.GetByIdAsync(id))!;
            Check(TestResultRules.IsSigned(result) == test.Signed, "Domain signature predicate");
            Check(await repo.AnySignedForOrderAsync([sample], []) == test.Signed, "Cancel protection via sample link");
            Check(await repo.AnySignedForOrderAsync([], [order]) == test.Signed, "Cancel protection via order link");
            if (test.Signed)
            {
                try { await service.UpdateAsync(id, new UpdateTestResultDto { Status = "Entered" }); throw new Exception("Signed service update accepted"); }
                catch (ConflictStateException) { passed++; }
            }
            Check(await repo.UpdateFieldsAsync(id, new TestResultFieldChanges(ReagentCode: "check")) == !test.Signed, "Atomic update signature guard");
            Check(await repo.SoftDeleteAsync(id) == !test.Signed, "Atomic delete signature guard");
            if (test.Signed)
            {
                await results.UpdateOneAsync(new BsonDocument("_id", id), Builders<BsonDocument>.Update.Set("isDeleted", true));
                Check(!await repo.AnySignedForOrderAsync([sample], [order]), "Deleted signed result must not block cancellation");
            }
        }
    Check(!await repo.AnySignedForOrderAsync([], []), "Empty links cannot match results");
    Console.WriteLine($"PASS: {passed} legacy result safety checks; disposable database only.");
}
finally
{
    await client.DropDatabaseAsync(dbName);
}
