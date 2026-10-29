// Đợt 3 step 2 checks: UC32 test orders with ICD-10 (TIEU-CHI Đợt 3 Bước 1, SMK-BS-02..06, QĐ10).
// ICD-10 required + format (400 MSG-ERR-DOC-01, nothing saved), DoctorId from the token, catalog prices,
// AwaitingPayment / STAT deferred payment, drafts without ICD-10 + submit (Quy trình Đợt 3A Q4), tube colors, cancel rules (own / other doctor / collected / non-cancel
// target / Manager exception), races, 401/403. Writes only to a disposable database.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BMS.Api.Controllers.Laboratory;
using BMS.Api.Middleware;
using BMS.Application.DTOs.Billing;
using BMS.Application.DTOs.Laboratory;
using BMS.Application.DTOs.TestResults;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Constants;
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
var databaseName = "BMS_UC32_Checks_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(databaseName);
var clock = new MutableClock(new DateTimeOffset(2026, 10, 30, 1, 0, 0, TimeSpan.Zero));
var passed = 0;
var failed = 0;
try
{
    await new FlowIndexInitializer(db, NullLogger<FlowIndexInitializer>.Instance).EnsureAsync(CancellationToken.None);
    var names = new MongoCollections("accounts", "roles", "refresh_tokens");
    var pkgs = db.GetCollection<ServicePackage>(names.ServicePackages);
    await pkgs.InsertManyAsync(new[]
    {
        Pkg("CBC", "Công thức máu (CBC)", 150_000m, "Huyết học"),
        Pkg("BIO", "Sinh hóa máu cơ bản", 250_000m, "Sinh hóa"),
        Pkg("GLU", "Glucose máu", 50_000m, "Sinh hóa"),
        Pkg("OLD", "Gói ngừng", 99_000m, "Sinh hóa", active: false),
    });
    await db.GetCollection<Patient>("patients").InsertOneAsync(new Patient
    { Id = "PAT-261030-0001", FullName = "Trần Thị Bích", DateOfBirth = new DateTime(1990, 5, 1, 0, 0, 0, DateTimeKind.Utc), GenderString = "Female", PhoneNumber = "0912345678" });
    var orderCol = db.GetCollection<TestOrder>(TestOrderRepository.CollectionName);
    var seq = new MongoSequenceGenerator(db);
    TestOrderService NewSvc() => new(new TestOrderRepository(db), new PatientRepository(db), FakeUsers.Create(),
        new ServicePackageRepository(db, names), seq, new SpecimenRepository(db), new TestResultRepository(db),
        new AuditLogService(new AuditLogRepository(db)), clock, NullLogger<TestOrderService>.Instance);
    var auditCol = db.GetCollection<AuditLog>(AuditLogRepository.CollectionName);
    var resultCol = db.GetCollection<TestResult>("test_results");
    var svc = NewSvc();
    CreateTestOrderRequestDto Req(string? icd, params string[] codes) => new()
    {
        PatientId = "PAT-261030-0001", ClinicalIndication = "Theo dõi đái tháo đường", Icd10Code = icd,
        Items = codes.Select(c => new CreateTestOrderItemDto { TestCode = c, Price = 1m }).ToList(), DoctorId = "forged-doctor"
    };

    await Check("TestOrderService/Repository have no BmsDbContext dependency", () =>
    {
        Assert(typeof(TestOrderService).GetConstructors().Single().GetParameters().All(p => p.ParameterType != typeof(BmsDbContext)));
        Assert(typeof(TestOrderRepository).GetConstructors().Single().GetParameters().All(p => p.ParameterType != typeof(BmsDbContext)));
        return Task.CompletedTask;
    });

    await Check("valid ICD E11.9 -> AwaitingPayment, ORD-20261030-0001, doctor from token, catalog prices (client price ignored)", async () =>
    {
        var o = await svc.CreateTestOrderAsync(Req("E11.9", "CBC", "BIO"), "doc-1");
        Assert(o.Status == "AwaitingPayment" && !o.PaymentDeferred, o.Status);
        Assert(o.OrderNumber == "ORD-20261030-0001", o.OrderNumber);
        Assert(o.DoctorId == "doc-1" && o.DoctorName == "BS doc-1");
        Assert(o.TotalAmount == 400_000m && o.Items.Single(i => i.TestCode == "CBC").Price == 150_000m);
        Assert(o.Icd10Codes.SequenceEqual(new[] { "E11.9" }) && o.PatientAge == 36 && o.PatientName == "Trần Thị Bích");
    });

    await Check("ICD missing / XYZ / E1 / E11.999 / 11.9 -> 400 MSG-ERR-DOC-01 and nothing saved", async () =>
    {
        var before = await orderCol.CountDocumentsAsync(FilterDefinition<TestOrder>.Empty);
        foreach (var bad in new string?[] { null, "", "   ", "XYZ", "E1", "E11.999", "11.9", "E11-9" })
        {
            var ex = await Throws<DomainValidationException>(() => svc.CreateTestOrderAsync(Req(bad, "CBC"), "doc-1"));
            Assert(ex.StatusCode == 400 && ex.Code == "MSG-ERR-DOC-01", bad);
        }
        var multi = Req("E11.9", "CBC"); multi.Icd10Codes = new() { "I10", "BAD" };
        Assert((await Throws<DomainValidationException>(() => svc.CreateTestOrderAsync(multi, "doc-1"))).Code == "MSG-ERR-DOC-01");
        Assert(await orderCol.CountDocumentsAsync(FilterDefinition<TestOrder>.Empty) == before);
    });

    await Check("ICD accepted: E11, I10, j45.909 is rejected (3 decimals) while j45.9 normalizes to J45.9; list merges + dedups", async () =>
    {
        var r = Req("j45.9", "GLU"); r.Icd10Codes = new() { "E11", "I10", "J45.9" };
        var o = await svc.CreateTestOrderAsync(r, "doc-1");
        Assert(o.Icd10Codes.OrderBy(x => x).SequenceEqual(new[] { "E11", "I10", "J45.9" }), string.Join(",", o.Icd10Codes));
        Assert((await Throws<DomainValidationException>(() => svc.CreateTestOrderAsync(Req("J45.909", "GLU"), "doc-1"))).Code == "MSG-ERR-DOC-01");
    });

    await Check("unknown or inactive test code -> 400, never a default price, nothing saved", async () =>
    {
        var before = await orderCol.CountDocumentsAsync(FilterDefinition<TestOrder>.Empty);
        Assert((await Throws<DomainValidationException>(() => svc.CreateTestOrderAsync(Req("E11.9", "CBC", "NOPE"), "doc-1"))).Code == "ORDER_TEST_NOT_IN_CATALOG");
        Assert((await Throws<DomainValidationException>(() => svc.CreateTestOrderAsync(Req("E11.9", "OLD"), "doc-1"))).Code == "ORDER_TEST_NOT_IN_CATALOG");
        Assert(await orderCol.CountDocumentsAsync(FilterDefinition<TestOrder>.Empty) == before);
        await Throws<ResourceNotFoundException>(() => svc.CreateTestOrderAsync(new CreateTestOrderRequestDto
        { PatientId = "PAT-NOPE", ClinicalIndication = "abcde", Icd10Code = "E11.9", Items = new() { new() { TestCode = "CBC" } } }, "doc-1"));
    });

    await Check("STAT -> AwaitingPayment + PaymentDeferred (sampling allowed); Routine not collectable before payment", async () =>
    {
        var r = Req("R50.9", "CBC"); r.Priority = "STAT";
        var stat = await svc.CreateTestOrderAsync(r, "doc-1");
        Assert(stat.Status == "AwaitingPayment" && stat.PaymentDeferred && stat.Priority == "STAT");
        var statEntity = await orderCol.Find(o => o.Id == stat.Id).SingleAsync();
        Assert(TestOrderStatusRules.CanCollectSample(statEntity));
        var routine = await orderCol.Find(o => o.OrderNumber == "ORD-20261030-0001").SingleAsync();
        Assert(!TestOrderStatusRules.CanCollectSample(routine));
        routine.Status = TestOrderStatus.PaidPendingSample;
        Assert(TestOrderStatusRules.CanCollectSample(routine));
        Assert(await db.GetCollection<BsonDocument>("samples").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty) == 0);
        var bad = Req("E11.9", "CBC"); bad.Priority = "ASAP";
        Assert((await Throws<DomainValidationException>(() => svc.CreateTestOrderAsync(bad, "doc-1"))).Code == "ORDER_PRIORITY_INVALID");
    });

    await Check("tubes by CLSI order of draw: Glucose -> Gray, CBC -> Purple, BIO -> Red (SST), PT -> LightBlue", async () =>
    {
        var o = await svc.CreateTestOrderAsync(Req("E11.9", "GLU", "CBC", "BIO"), "doc-1");
        Assert(o.Items.Single(i => i.TestCode == "GLU").TubeColor == "Gray");
        Assert(o.Items.Single(i => i.TestCode == "CBC").TubeColor == "Purple");
        Assert(o.Items.Single(i => i.TestCode == "BIO").TubeColor == "Red");
        Assert(TubeRules.For("PT", "Prothrombin time").Color == "LightBlue" && TubeRules.For("APTT").Color == "LightBlue");
        Assert(TubeRules.For("SEPTIC-PANEL").Color == "Red"); // "PT" must not match inside other words
        Assert(TubeRules.Fluoride.DrawOrder == 6 && TubeRules.Edta.DrawOrder == 5 && TubeRules.Citrate.DrawOrder == 2);
    });

    // ---------- Quy trình Đợt 3A Q4: drafts without ICD-10, ICD-10 required on submit (before fee calculation) ----------
    var billing = new BillingSettingsService(new BillingSettingsRepository(db), new AuditLogService(new AuditLogRepository(db)), clock);
    var fees = new CalculateFeeService(new ServicePackageRepository(db, names), new TestOrderRepository(db), billing, clock);
    var pay = new OrderPaymentService(new TestOrderRepository(db), new InvoiceRepository(db), new PatientRepository(db), seq, billing, clock,
        NullLogger<OrderPaymentService>.Instance);
    CreateTestOrderRequestDto Draft(string? icd, params string[] codes) { var r = Req(icd, codes); r.SaveAsDraft = true; return r; }

    await Check("draft: saved without ICD-10 (Draft, no codes); malformed ICD on a draft still 400; draft not collectable", async () =>
    {
        var d = await svc.CreateTestOrderAsync(Draft(null, "CBC"), "doc-1");
        Assert(d.Status == "Draft" && d.Icd10Codes.Count == 0 && !d.PaymentDeferred, d.Status);
        var blank = Draft("  ", "CBC");
        Assert((await svc.CreateTestOrderAsync(blank, "doc-1")).Status == "Draft");
        Assert((await Throws<DomainValidationException>(() => svc.CreateTestOrderAsync(Draft("XYZ", "CBC"), "doc-1"))).Code == "MSG-ERR-DOC-01");
        Assert(!TestOrderStatusRules.CanCollectSample((await orderCol.Find(x => x.Id == d.Id).SingleAsync())));
        // Non-draft still requires ICD-10 (unchanged rule).
        Assert((await Throws<DomainValidationException>(() => svc.CreateTestOrderAsync(Req(null, "CBC"), "doc-1"))).Code == "MSG-ERR-DOC-01");
    });

    await Check("draft: no fee before submit -> estimate/quote/pay/vnpay 409 ORDER_NOT_SUBMITTED, no invoice written", async () =>
    {
        var d = await svc.CreateTestOrderAsync(Draft(null, "CBC", "BIO"), "doc-1");
        Assert((await Throws<ConflictStateException>(() => fees.EstimateOrderFeeAsync(d.Id, null, null, false))).Code == "ORDER_NOT_SUBMITTED");
        Assert((await Throws<ConflictStateException>(() => pay.QuoteAsync(d.Id, new OrderPaymentRequestDto { PaymentMethod = "Cash" }))).Code == "ORDER_NOT_SUBMITTED");
        Assert((await Throws<ConflictStateException>(() => pay.PayAtCounterAsync(d.Id, new OrderPaymentRequestDto { PaymentMethod = "Cash" }, "rec-1", null))).Code == "ORDER_NOT_SUBMITTED");
        Assert((await Throws<ConflictStateException>(() => pay.StartVnPayAsync(d.Id, new OrderPaymentRequestDto { PaymentMethod = "VNPay" }, "rec-1", false))).Code == "ORDER_NOT_SUBMITTED");
        Assert(await db.GetCollection<Invoice>("invoices").CountDocumentsAsync(i => i.OrderId == d.Id) == 0);
        // A submitted order that somehow has no ICD-10 (legacy row) is not billable either.
        var legacy = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-1");
        await orderCol.UpdateOneAsync(x => x.Id == legacy.Id, Builders<TestOrder>.Update.Set(x => x.Icd10Codes, new List<string>()));
        Assert((await Throws<DomainValidationException>(() => pay.QuoteAsync(legacy.Id, new OrderPaymentRequestDto()))).Code == "MSG-ERR-DOC-01");
    });

    await Check("submit: no ICD -> 400 MSG-ERR-DOC-01 (stays Draft); other doctor 403; 'e11.9' -> AwaitingPayment [E11.9] then fee 400,000; again 409; STAT -> deferred", async () =>
    {
        var d = await svc.CreateTestOrderAsync(Draft(null, "CBC", "BIO"), "doc-1");
        Assert((await Throws<DomainValidationException>(() => svc.SubmitDraftAsync(d.Id, new(), "doc-1"))).Code == "MSG-ERR-DOC-01");
        Assert((await Throws<DomainValidationException>(() => svc.SubmitDraftAsync(d.Id, new() { Icd10Code = "E11.999" }, "doc-1"))).Code == "MSG-ERR-DOC-01");
        Assert((await orderCol.Find(x => x.Id == d.Id).SingleAsync()).Status == TestOrderStatus.Draft);
        Assert((await Throws<ForbiddenAccessException>(() => svc.SubmitDraftAsync(d.Id, new() { Icd10Code = "E11.9" }, "doc-2"))).StatusCode == 403);
        var s1 = await svc.SubmitDraftAsync(d.OrderNumber, new() { Icd10Code = " e11.9 " }, "doc-1");
        Assert(s1.Status == "AwaitingPayment" && s1.Icd10Codes.SequenceEqual(new[] { "E11.9" }) && !s1.PaymentDeferred);
        Assert((await fees.EstimateOrderFeeAsync(d.Id, null, null, false))!.NetTotalPayable == 400_000m);
        Assert((await Throws<ConflictStateException>(() => svc.SubmitDraftAsync(d.Id, new() { Icd10Code = "E11.9" }, "doc-1"))).Code == "ORDER_NOT_DRAFT");
        // Draft that already had a code: submit needs nothing more.
        var withCode = await svc.CreateTestOrderAsync(Draft("I10", "GLU"), "doc-1");
        Assert((await svc.SubmitDraftAsync(withCode.Id, new(), "doc-1")).Icd10Codes.SequenceEqual(new[] { "I10" }));
        var statDraft = Draft(null, "CBC"); statDraft.Priority = "STAT";
        var sd = await svc.CreateTestOrderAsync(statDraft, "doc-1");
        Assert(!sd.PaymentDeferred);
        var ss = await svc.SubmitDraftAsync(sd.Id, new() { Icd10Codes = new() { "R50.9" } }, "doc-1");
        Assert(ss.PaymentDeferred && TestOrderStatusRules.CanCollectSample((await orderCol.Find(x => x.Id == sd.Id).SingleAsync())));
    });

    await Check("HTTP submit: 401 anon; 403 Admin/Manager/Lab/Receptionist/Patient; Doctor no ICD 400; Doctor 200", async () =>
    {
        await using var app = await StartAsync(s => s.AddScoped<ITestOrderService>(_ => NewSvc()));
        using var http = Client(app);
        var d = await svc.CreateTestOrderAsync(Draft(null, "CBC"), "check-Doctor");
        var url = $"/api/TestOrders/{d.Id}/submit";
        Assert((await http.PostAsJsonAsync(url, new { icd10Code = "E11.9" })).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Admin", "Manager", "Lab", "Receptionist", "Patient" })
            Assert((await As(http, role).PostAsJsonAsync(url, new { icd10Code = "E11.9" })).StatusCode == HttpStatusCode.Forbidden, role);
        var noIcd = await As(http, "Doctor").PostAsJsonAsync(url, new { });
        Assert(noIcd.StatusCode == HttpStatusCode.BadRequest && (await noIcd.Content.ReadAsStringAsync()).Contains("MSG-ERR-DOC-01"));
        var ok = await As(http, "Doctor").PostAsJsonAsync(url, new { icd10Code = "E11.9" });
        var t = await ok.Content.ReadAsStringAsync();
        Assert(ok.StatusCode == HttpStatusCode.OK && t.Contains("\"status\":\"AwaitingPayment\""), t);
        await app.StopAsync();
    });

    var cancel = new UpdateTestOrderStatusRequestDto { Status = "Cancelled", Notes = "BN đổi ý" };
    await Check("cancel: own uncollected -> Cancelled; other doctor -> 403; non-Cancelled target -> 403; again -> 409", async () =>
    {
        var o = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-1");
        Assert((await Throws<ForbiddenAccessException>(() => svc.UpdateOrderStatusAsync(o.Id, cancel, "doc-2", false))).StatusCode == 403);
        foreach (var target in new[] { "Completed", "PaidPendingSample", "SampleCollected", "InAnalysis", "AwaitingPayment" })
            Assert((await Throws<ForbiddenAccessException>(() => svc.UpdateOrderStatusAsync(o.Id, new() { Status = target }, "doc-1", false))).StatusCode == 403);
        Assert((await Throws<ForbiddenAccessException>(() => svc.UpdateOrderStatusAsync(o.Id, new() { Status = "Completed" }, "mgr-1", true))).StatusCode == 403);
        var done = await svc.UpdateOrderStatusAsync(o.OrderNumber, cancel, "doc-1", false);
        Assert(done.Status == "Cancelled");
        var stored = await orderCol.Find(x => x.Id == o.Id).SingleAsync();
        Assert(stored.StatusChangedBy == "doc-1" && stored.StatusNote == "BN đổi ý");
        Assert((await Throws<ConflictStateException>(() => svc.UpdateOrderStatusAsync(o.Id, cancel, "doc-1", false))).Code == "ORDER_ALREADY_CANCELLED");
        Assert((await Throws<DomainValidationException>(() => svc.UpdateOrderStatusAsync(o.Id, new() { Status = "Bogus" }, "doc-1", false))).StatusCode == 400);
        await Throws<ResourceNotFoundException>(() => svc.UpdateOrderStatusAsync("nope", cancel, "doc-1", false));
    });

    await Check("cancel: PaidPendingSample own -> ok; sample collected -> 409 for doctor; Manager exception cancel -> ok; Completed -> 409", async () =>
    {
        var paid = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-1");
        await orderCol.UpdateOneAsync(x => x.Id == paid.Id, Builders<TestOrder>.Update.Set(x => x.Status, TestOrderStatus.PaidPendingSample));
        Assert((await svc.UpdateOrderStatusAsync(paid.Id, cancel, "doc-1", false)).Status == "Cancelled");
        var collected = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-1");
        await orderCol.UpdateOneAsync(x => x.Id == collected.Id, Builders<TestOrder>.Update.Set(x => x.Status, TestOrderStatus.SampleCollected));
        var ex = await Throws<ConflictStateException>(() => svc.UpdateOrderStatusAsync(collected.Id, cancel, "doc-1", false));
        Assert(ex.StatusCode == 409 && ex.Code == "ORDER_SAMPLE_COLLECTED");
        Assert((await svc.UpdateOrderStatusAsync(collected.Id, cancel, "mgr-1", true)).Status == "Cancelled");
        var completed = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-2");
        await orderCol.UpdateOneAsync(x => x.Id == completed.Id, Builders<TestOrder>.Update.Set(x => x.Status, TestOrderStatus.Completed));
        Assert((await Throws<ConflictStateException>(() => svc.UpdateOrderStatusAsync(completed.Id, cancel, "mgr-1", true))).Code == "ORDER_COMPLETED");
    });

    await Check("Manager cancel (Q5): reason required; collected/InAnalysis ok until a result is signed; signed -> 409 ORDER_RESULT_SIGNED; audited", async () =>
    {
        async Task<TestOrderResponseDto> At(TestOrderStatus st)
        {
            var o = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-1");
            await orderCol.UpdateOneAsync(x => x.Id == o.Id, Builders<TestOrder>.Update.Set(x => x.Status, st));
            return o;
        }
        var reason = new UpdateTestOrderStatusRequestDto { Status = "Cancelled", Notes = "Mẫu vỡ, BN không quay lại" };
        var a = await At(TestOrderStatus.SampleCollected);
        foreach (var bad in new string?[] { null, "", "   ", "abc" })
            Assert((await Throws<DomainValidationException>(() => svc.UpdateOrderStatusAsync(a.Id, new() { Status = "Cancelled", Notes = bad }, "mgr-1", true))).Code == "CANCEL_REASON_REQUIRED", bad);
        Assert((await orderCol.Find(x => x.Id == a.Id).SingleAsync()).Status == TestOrderStatus.SampleCollected);
        Assert((await svc.UpdateOrderStatusAsync(a.Id, reason, "mgr-1", true)).Status == "Cancelled");
        var stored = await orderCol.Find(x => x.Id == a.Id).SingleAsync();
        Assert(stored.StatusChangedBy == "mgr-1" && stored.StatusNote == "Mẫu vỡ, BN không quay lại");
        var log = await auditCol.Find(l => l.EntityId == a.OrderNumber && l.Action == "ORDER_CANCELLED").SingleAsync();
        Assert(log.UserId == "mgr-1" && log.UserRole == "Manager" && log.Severity == "WARNING" && log.Details.Contains("SampleCollected") && log.Details.Contains("Mẫu vỡ"), log.Details);

        // InAnalysis with an unsigned (entered) result -> still cancellable.
        var b = await At(TestOrderStatus.InAnalysis);
        await db.GetCollection<Sample>(SpecimenRepository.CollectionName).InsertOneAsync(new Sample { Id = "smp-b", Barcode = "SMP-20261030-9001", SmpDay = "2026-10-30", TestOrderId = b.OrderNumber });
        await resultCol.InsertOneAsync(new TestResult { Id = "res-b", SampleId = "SMP-20261030-9001", ValidationStatus = ResultValidationStatus.Entered });
        Assert((await svc.UpdateOrderStatusAsync(b.Id, reason, "mgr-1", true)).Status == "Cancelled");

        // Signed via sample id, via barcode, via appointmentId = order number, or ApprovedAt only -> 409 and nothing changes.
        var signedCases = new (string Sample, string? SampleId, string? Appointment, ResultValidationStatus Vs, DateTime? ApprovedAt)[]
        {
            ("smp-c1", "smp-c1", null, ResultValidationStatus.DoctorApproved, null),
            ("smp-c2", "BARCODE", null, ResultValidationStatus.DoctorApproved, DateTime.UtcNow),
            ("smp-c3", null, "ORDER", ResultValidationStatus.TechnicianVerified, DateTime.UtcNow),
        };
        var n = 0;
        foreach (var sc in signedCases)
        {
            var o = await At(TestOrderStatus.InAnalysis);
            n++;
            var barcode = $"SMP-20261030-910{n}";
            await db.GetCollection<Sample>(SpecimenRepository.CollectionName).InsertOneAsync(new Sample { Id = sc.Sample, Barcode = barcode, SmpDay = "2026-10-30", TestOrderId = o.OrderNumber });
            await resultCol.InsertOneAsync(new TestResult { Id = "res-c" + n, SampleId = sc.SampleId == "BARCODE" ? barcode : sc.SampleId ?? "", AppointmentId = sc.Appointment == "ORDER" ? o.OrderNumber : "", ValidationStatus = sc.Vs, ApprovedAt = sc.ApprovedAt });
            var ex = await Throws<ConflictStateException>(() => svc.UpdateOrderStatusAsync(o.Id, reason, "mgr-1", true));
            Assert(ex.Code == "ORDER_RESULT_SIGNED" && ex.StatusCode == 409, $"case {n}: {ex.Code}");
            Assert((await orderCol.Find(x => x.Id == o.Id).SingleAsync()).Status == TestOrderStatus.InAnalysis);
        }
        // A soft-deleted signed result does not block.
        var d = await At(TestOrderStatus.SampleCollected);
        await resultCol.InsertOneAsync(new TestResult { Id = "res-del", AppointmentId = d.OrderNumber, ValidationStatus = ResultValidationStatus.DoctorApproved, IsDeleted = true });
        Assert((await svc.UpdateOrderStatusAsync(d.Id, reason, "mgr-1", true)).Status == "Cancelled");
        // Doctor cancels are audited too (reason optional for the doctor).
        var e = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-1");
        await svc.UpdateOrderStatusAsync(e.Id, new() { Status = "Cancelled" }, "doc-1", false);
        Assert((await auditCol.Find(l => l.EntityId == e.OrderNumber).SingleAsync()).UserRole == "Doctor");
    });

    await Check("signed results are read-only: update/delete/re-approve -> 409 RESULT_SIGNED_READ_ONLY; unsigned update ok", async () =>
    {
        var rs = new TestResultService(new TestResultRepository(db), NotCalled<IAppointmentRepository>(), NotCalled<INotificationRepository>(), NullLogger<TestResultService>.Instance);
        await resultCol.InsertOneAsync(new TestResult { Id = "res-ro", TestResultId = "TRE-RO", ValidationStatus = ResultValidationStatus.DoctorApproved, ApprovedAt = DateTime.UtcNow, ReagentCode = "R1" });
        Assert((await Throws<ConflictStateException>(() => rs.UpdateAsync("res-ro", new() { ReagentCode = "R2" }))).Code == "RESULT_SIGNED_READ_ONLY");
        Assert((await Throws<ConflictStateException>(() => rs.DeleteAsync("TRE-RO"))).Code == "RESULT_SIGNED_READ_ONLY");
        Assert((await Throws<ConflictStateException>(() => rs.ApproveAsync("res-ro", new() { ApproverId = "mgr-2" }))).Code == "RESULT_SIGNED_READ_ONLY");
        var after = await resultCol.Find(x => x.Id == "res-ro").SingleAsync();
        Assert(after.ReagentCode == "R1" && !after.IsDeleted && after.ApprovedByDoctorId == null);
        // Repository refuses atomically even if the service check is bypassed.
        Assert(!await new TestResultRepository(db).UpdateFieldsAsync("res-ro", new TestResultFieldChanges(ReagentCode: "R3")));
        await resultCol.InsertOneAsync(new TestResult { Id = "res-rw", ValidationStatus = ResultValidationStatus.Entered, ReagentCode = "R1" });
        await rs.UpdateAsync("res-rw", new() { ReagentCode = "R2" });
        Assert((await resultCol.Find(x => x.Id == "res-rw").SingleAsync()).ReagentCode == "R2");
    });

    await Check("race: 20 parallel cancels by the owner -> exactly 1 success, 19 x 409", async () =>
    {
        var o = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-1");
        var res = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            try { await NewSvc().UpdateOrderStatusAsync(o.Id, cancel, "doc-1", false); return "ok"; }
            catch (ConflictStateException) { return "409"; }
        })));
        Assert(res.Count(r => r == "ok") == 1 && res.Count(r => r == "409") == 19, string.Join(",", res));
    });

    await Check("race: cancel vs. sample collection on the same order -> exactly one wins, no Cancelled+collected mix", async () =>
    {
        for (var round = 0; round < 10; round++)
        {
            var o = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-1");
            await orderCol.UpdateOneAsync(x => x.Id == o.Id, Builders<TestOrder>.Update.Set(x => x.Status, TestOrderStatus.PaidPendingSample));
            var repo = new TestOrderRepository(db);
            var collect = Task.Run(() => repo.TryTransitionAsync(o.Id, new[] { TestOrderStatus.PaidPendingSample }, TestOrderStatus.SampleCollected, "lab-1", DateTime.UtcNow, null));
            var cancelTask = Task.Run(async () =>
            {
                try { await NewSvc().UpdateOrderStatusAsync(o.Id, cancel, "doc-1", false); return true; }
                catch (ConflictStateException) { return false; }
            });
            var collectedWon = await collect != null;
            var cancelWon = await cancelTask;
            Assert(collectedWon ^ cancelWon, $"round {round}: collected={collectedWon} cancelled={cancelWon}");
            var final = (await orderCol.Find(x => x.Id == o.Id).SingleAsync()).Status;
            Assert(final == (collectedWon ? TestOrderStatus.SampleCollected : TestOrderStatus.Cancelled));
        }
    });

    await Check("60 parallel orders -> 60 distinct ORD- numbers (unique index ux_test_orders_number)", async () =>
    {
        clock.Set(new DateTimeOffset(2026, 10, 31, 2, 0, 0, TimeSpan.Zero));
        var created = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => Task.Run(() => NewSvc().CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-1"))));
        var nums = created.Select(c => c.OrderNumber).ToList();
        Assert(nums.Distinct().Count() == 60 && nums.All(n => n.StartsWith("ORD-20261031-", StringComparison.Ordinal)));
        Console.WriteLine($"     concurrency: 60 parallel orders -> {nums.Distinct().Count()} distinct numbers");
        var idx = await (await orderCol.Indexes.ListAsync()).ToListAsync();
        Assert(idx.Any(i => i["name"] == "ux_test_orders_number" && i.GetValue("unique", false).ToBoolean()));
        Assert(idx.Any(i => i["name"] == "ix_test_orders_patient_created") && idx.Any(i => i["name"] == "ix_test_orders_status_created"));
    });

    await Check("HTTP: POST 401 anonymous, 403 Admin/Manager/Lab/Receptionist/Patient, Doctor 201 (forged DoctorId ignored)", async () =>
    {
        await using var app = await StartAsync(s => s.AddScoped<ITestOrderService>(_ => NewSvc()));
        using var http = Client(app);
        var body = new { patientId = "PAT-261030-0001", clinicalIndication = "Theo dõi ĐTĐ", icd10Code = "E11.9", doctorId = "forged", items = new[] { new { testCode = "CBC", price = 1 } } };
        Assert((await http.PostAsJsonAsync("/api/TestOrders", body)).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Admin", "Manager", "Lab", "Receptionist", "Patient" })
            Assert((await As(http, role).PostAsJsonAsync("/api/TestOrders", body)).StatusCode == HttpStatusCode.Forbidden, role);
        var ok = await As(http, "Doctor").PostAsJsonAsync("/api/TestOrders", body);
        var text = await ok.Content.ReadAsStringAsync();
        Assert(ok.StatusCode == HttpStatusCode.Created, text);
        var data = JsonDocument.Parse(text).RootElement.GetProperty("data");
        Assert(data.GetProperty("doctorId").GetString() == "check-Doctor" && data.GetProperty("status").GetString() == "AwaitingPayment");
        Assert(data.GetProperty("totalAmount").GetDecimal() == 150_000m);
        await app.StopAsync();
    });

    await Check("HTTP: missing ICD -> 400 with code MSG-ERR-DOC-01; XYZ -> 400; no exception text leaked", async () =>
    {
        await using var app = await StartAsync(s => s.AddScoped<ITestOrderService>(_ => NewSvc()));
        using var http = Client(app);
        foreach (var icd in new[] { (string?)null, "XYZ" })
        {
            var res = await As(http, "Doctor").PostAsJsonAsync("/api/TestOrders", new { patientId = "PAT-261030-0001", clinicalIndication = "Theo dõi ĐTĐ", icd10Code = icd, items = new[] { new { testCode = "CBC" } } });
            var t = await res.Content.ReadAsStringAsync();
            Assert(res.StatusCode == HttpStatusCode.BadRequest && t.Contains("MSG-ERR-DOC-01") && !t.Contains("   at "), t);
        }
        await app.StopAsync();
    });

    await Check("HTTP PUT status: 401 anon; 403 Lab/Receptionist/Admin/Patient; Doctor other's 403; own 200; collected 409; Manager 200", async () =>
    {
        await using var app = await StartAsync(s => s.AddScoped<ITestOrderService>(_ => NewSvc()));
        using var http = Client(app);
        var mine = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "check-Doctor");
        var others = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "doc-2");
        var collected = await svc.CreateTestOrderAsync(Req("E11.9", "CBC"), "check-Doctor");
        await orderCol.UpdateOneAsync(x => x.Id == collected.Id, Builders<TestOrder>.Update.Set(x => x.Status, TestOrderStatus.SampleCollected));
        var c = new { status = "Cancelled" };
        Assert((await http.PutAsJsonAsync($"/api/TestOrders/{mine.Id}/status", c)).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Lab", "Receptionist", "Admin", "Patient" })
            Assert((await As(http, role).PutAsJsonAsync($"/api/TestOrders/{mine.Id}/status", c)).StatusCode == HttpStatusCode.Forbidden, role);
        Assert((await As(http, "Doctor").PutAsJsonAsync($"/api/TestOrders/{others.Id}/status", c)).StatusCode == HttpStatusCode.Forbidden);
        Assert((await As(http, "Doctor").PutAsJsonAsync($"/api/TestOrders/{mine.Id}/status", new { status = "Completed" })).StatusCode == HttpStatusCode.Forbidden);
        Assert((await As(http, "Doctor").PutAsJsonAsync($"/api/TestOrders/{mine.Id}/status", c)).StatusCode == HttpStatusCode.OK);
        var conflict = await As(http, "Doctor").PutAsJsonAsync($"/api/TestOrders/{collected.Id}/status", c);
        Assert(conflict.StatusCode == HttpStatusCode.Conflict && (await conflict.Content.ReadAsStringAsync()).Contains("ORDER_SAMPLE_COLLECTED"));
        var noReason = await As(http, "Manager").PutAsJsonAsync($"/api/TestOrders/{others.Id}/status", c);
        Assert(noReason.StatusCode == HttpStatusCode.BadRequest && (await noReason.Content.ReadAsStringAsync()).Contains("CANCEL_REASON_REQUIRED"));
        Assert((await As(http, "Manager").PutAsJsonAsync($"/api/TestOrders/{others.Id}/status", new { status = "Cancelled", notes = "Sai bệnh nhân, hủy theo yêu cầu QL" })).StatusCode == HttpStatusCode.OK);
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
    if (databaseName.StartsWith("BMS_UC32_Checks_", StringComparison.Ordinal)) await client.DropDatabaseAsync(databaseName);
}
Console.WriteLine($"{passed} PASSED, {failed} FAILED");
return failed == 0 ? 0 : 1;

static ServicePackage Pkg(string code, string name, decimal price, string category, bool active = true) => new()
{ Id = ObjectId.GenerateNewId().ToString(), Code = code, Name = name, Price = price, Category = category, IsActive = active, CreatedAt = DateTime.UtcNow };

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
static T NotCalled<T>() where T : class => DispatchProxy.Create<T, NotCalledProxy>();
static async Task<WebApplication> StartAsync(Action<IServiceCollection> register)
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    builder.Services.AddControllers().AddApplicationPart(typeof(TestOrdersController).Assembly);
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

/// <summary>User repository fake: every id resolves to a doctor named "BS {id}"; other members must not be called.</summary>
public class FakeUsers : DispatchProxy
{
    public static IUserRepository Create() => DispatchProxy.Create<IUserRepository, FakeUsers>();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        "GetByIdAsync" or "FindByIdAsync" => Task.FromResult<User?>(new User { Id = (string)args![0]!, FullName = "BS " + args[0], RoleId = 4 }),
        _ => throw new InvalidOperationException($"{targetMethod?.Name} must not be called by this check.")
    };
}
