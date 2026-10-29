// Đợt 3 step 3 checks: UC53 fee formula (TIEU-CHI Đợt 3 Bước 2 numeric matrix, SMK-LT-07..23) and the UC55
// payment gate: BHYT by the 3rd card character, 379,500 threshold, BMS10 cap, rounding halves up, MSG-ERR-FIN-01,
// UC55 = UC53 numbers, counter payment -> PaidPendingSample exactly once, VNPay never Paid, 401/403.
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BMS.Api.Controllers.Billing;
using BMS.Api.Middleware;
using BMS.Application.DTOs.Billing;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Application.Services.Billing;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
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
var databaseName = "BMS_UC55_Checks_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(databaseName);
var clock = new MutableClock(new DateTimeOffset(2026, 10, 30, 2, 0, 0, TimeSpan.Zero));
var now = clock.GetUtcNow().UtcDateTime;
var passed = 0;
var failed = 0;
try
{
    await new FlowIndexInitializer(db, NullLogger<FlowIndexInitializer>.Instance).EnsureAsync(CancellationToken.None);
    FeeLine L(decimal price, string code = "X") => new(code, code, "Sinh hóa", price);
    var policy = FeePolicy.SeedDefault; // fresh database: 8 seeded BHYT group codes, priority discount 0
    decimal Net(string? card, bool home, string? voucher, params decimal[] prices) =>
        FeeCalculator.Compute(prices.Select((p, i) => L(p, "T" + i)).ToList(), home, card, voucher, false, policy, now).NetTotalPayable;
    const string DN4 = "DN4791234567890";

    await Check("matrix: DN4 150,000 -> 0; 379,499 -> 0; 379,500 -> 75,900 -> 76,000", () =>
    {
        Assert(Net(DN4, false, null, 150_000m) == 0m);
        Assert(Net(DN4, false, null, 379_499m) == 0m);
        var at = FeeCalculator.Compute(new[] { L(379_500m) }, false, DN4, null, false, policy, now);
        Assert(at.NetBeforeRounding == 75_900m && at.NetTotalPayable == 76_000m && at.InsuranceCoveragePercent == 80 && at.InsuranceBenefitPercent == 80);
        var below = FeeCalculator.Compute(new[] { L(150_000m) }, false, DN4, null, false, policy, now);
        Assert(below.InsuranceCoveragePercent == 100 && below.InsuranceBenefitPercent == 80 && below.InsuranceDeduction == 150_000m);
        return Task.CompletedTask;
    });
    await Check("matrix: DN4 300,000 + home 150,000 -> 150,000 (home surcharge never covered)", () =>
    {
        Assert(Net(DN4, true, null, 300_000m) == 150_000m);
        return Task.CompletedTask;
    });
    await Check("matrix: DN4 CBC 150k + BIO 250k + home + BMS10 -> 190,000 (threshold before voucher)", () =>
    {
        var r = FeeCalculator.Compute(new[] { L(150_000m, "CBC"), L(250_000m, "BIO") }, true, DN4, "BMS10", false, policy, now);
        Assert(r.GrossTotal == 550_000m && r.InsuranceDeduction == 320_000m && r.VoucherDiscount == 40_000m && r.NetTotalPayable == 190_000m, r.NetTotalPayable.ToString());
        return Task.CompletedTask;
    });
    await Check("matrix: 400k -> QN5/CA5/CY5 0; QN1/DK2/CN2/LH2/DN1 0; HT3 20,000; DN4/QN4/HT4 80,000; TE1/HN2/GD4 (not seeded) -> 400", async () =>
    {
        foreach (var g in new[] { "QN5", "CA5", "CY5", "QN1", "DK2", "CN2", "LH2", "DN1" })
            Assert(Net(g + "791234567890", false, null, 400_000m) == 0m, g);
        Assert(Net("HT3791234567890", false, null, 400_000m) == 20_000m);
        foreach (var g in new[] { "DN4", "QN4", "HT4" })
            Assert(Net(g + "791234567890", false, null, 400_000m) == 80_000m, g);
        // Quy trình Đợt 3A Q1: only catalog codes are accepted; TE/HN/GD are not in the seed (open point C2).
        foreach (var g in new[] { "TE1", "HN2", "GD4" })
            Assert((await Throws<DomainValidationException>(() => Task.FromResult(Net(g + "791234567890", false, null, 400_000m)))).Code == "MSG-ERR-FIN-01", g);
    });
    await Check("BMS10 on 2,010,000 -> discount capped at 200,000, pays 1,810,000; FREEHOME only with home", () =>
    {
        var r = FeeCalculator.Compute(new[] { L(2_010_000m) }, false, null, "BMS10", false, policy, now);
        Assert(r.VoucherDiscount == 200_000m && r.NetTotalPayable == 1_810_000m);
        var fh = FeeCalculator.Compute(new[] { L(100_000m) }, true, null, "FREEHOME", false, policy, now);
        Assert(fh.IsVoucherValid && fh.VoucherDiscount == 150_000m && fh.NetTotalPayable == 100_000m);
        var fc = FeeCalculator.Compute(new[] { L(100_000m) }, false, null, "FREEHOME", false, policy, now);
        Assert(!fc.IsVoucherValid && fc.VoucherDiscount == 0m);
        var bad = FeeCalculator.Compute(new[] { L(100_000m) }, false, null, "SPRING10", false, policy, now);
        Assert(!bad.IsVoucherValid && bad.NetTotalPayable == 100_000m);
        return Task.CompletedTask;
    });
    await Check("rounding halves up: 2,499 / 2,500 / 3,500 / 499 / 500 -> 2,000 / 3,000 / 4,000 / 0 / 1,000", () =>
    {
        Assert(FeeCalculator.RoundToThousand(2_499m) == 2_000m && FeeCalculator.RoundToThousand(2_500m) == 3_000m && FeeCalculator.RoundToThousand(3_500m) == 4_000m);
        Assert(FeeCalculator.RoundToThousand(499m) == 0m && FeeCalculator.RoundToThousand(500m) == 1_000m);
        Assert(Net(null, false, null, 2_500m) == 3_000m && Net(null, false, null, 2_499m) == 2_000m && Net(null, false, null, 3_500m) == 4_000m);
        Assert(FeeCalculator.Compute(new[] { L(2_500m) }, false, null, null, false, policy, now).TotalInWords.Contains("Ba nghìn đồng chẵn"));
        return Task.CompletedTask;
    });
    await Check("card rejected only for the 4 format errors -> 400 MSG-ERR-FIN-01; never a default 80%", async () =>
    {
        foreach (var bad in new[] { "DN479123456789", "DN47912345678901", "ZZ4791234567890", "DN6791234567890", "DN0791234567890", "DN4A91234567890", "DN47B1234567890" })
        {
            var ex = await Throws<DomainValidationException>(() => Task.FromResult(BhytCard.Parse(bad, policy.BhytGroupCodes)));
            Assert(ex.Code == "MSG-ERR-FIN-01" && ex.StatusCode == 400, bad);
        }
        Assert(BhytCard.Parse(" dn4-79 1234567890 ", policy.BhytGroupCodes).Card == DN4);
        Assert(BhytCard.Parse("LH2791234567890", policy.BhytGroupCodes).BenefitPercent == 100 && BhytCard.Parse("HT3791234567890", policy.BhytGroupCodes).BenefitPercent == 95);
    });

    // ---------- catalog-backed services ----------
    var names = new MongoCollections("accounts", "roles", "refresh_tokens");
    await db.GetCollection<ServicePackage>(names.ServicePackages).InsertManyAsync(new[]
    {
        new ServicePackage { Id = ObjectId.GenerateNewId().ToString(), Code = "CBC", Name = "CBC", Price = 150_000m, Category = "Huyết học", IsActive = true },
        new ServicePackage { Id = ObjectId.GenerateNewId().ToString(), Code = "BIO", Name = "Sinh hóa", Price = 250_000m, Category = "Sinh hóa", IsActive = true },
    });
    var orderRepo = new TestOrderRepository(db);
    var settingsRepo = new BillingSettingsRepository(db);
    var auditCol = db.GetCollection<AuditLog>(AuditLogRepository.CollectionName);
    var billingSettings = new BillingSettingsService(settingsRepo, new AuditLogService(new AuditLogRepository(db)), clock);
    var fees = new CalculateFeeService(new ServicePackageRepository(db, names), orderRepo, billingSettings, clock);
    var admin = new Actor("adm-1", "Admin A", "Admin");
    var manager = new Actor("mgr-1", "QL B", "Manager");

    await Check("BHYT catalog seeded once with exactly QN, CA, CY, DK, CN, HT, LH, DN; re-seed is idempotent", async () =>
    {
        var rows = await settingsRepo.GetBhytGroupCodesAsync();
        Assert(rows.Select(r => r.Code).OrderBy(c => c).SequenceEqual(new[] { "CA", "CN", "CY", "DK", "DN", "HT", "LH", "QN" }), string.Join(",", rows.Select(r => r.Code)));
        Assert(rows.All(r => r.IsActive && r.Seeded));
        Assert(await settingsRepo.SeedBhytGroupCodesAsync(BhytCard.SeedGroupCodes, now) == 0);
        Assert((await billingSettings.GetAsync()).BhytGroupCodes.SetEquals(FeePolicy.SeedDefault.BhytGroupCodes));
    });

    await Check("BHYT catalog edit: ' te ' normalized to TE -> TE1 card accepted; deactivate DN -> DN4 rejected, restart seed keeps it off; bad codes 400; audited", async () =>
    {
        var req1 = new CalculateFeeRequestDto { PackageIds = new() { "CBC" }, InsuranceCardNumber = "TE1012345678901" };
        Assert((await Throws<DomainValidationException>(() => fees.CalculateFeeAsync(req1))).Code == "MSG-ERR-FIN-01");
        var te = await billingSettings.UpsertBhytGroupCodeAsync(" te ", new() { Description = "Trẻ em dưới 6 tuổi", IsActive = true }, admin);
        Assert(te.Code == "TE" && te.IsActive && !te.Seeded && te.UpdatedBy == "adm-1");
        Assert((await fees.CalculateFeeAsync(req1)).InsuranceCoveragePercent == 100);
        await billingSettings.UpsertBhytGroupCodeAsync("dn", new() { IsActive = false }, manager);
        var req2 = new CalculateFeeRequestDto { PackageIds = new() { "CBC" }, InsuranceCardNumber = DN4 };
        Assert((await Throws<DomainValidationException>(() => fees.CalculateFeeAsync(req2))).Code == "MSG-ERR-FIN-01");
        await new FlowIndexInitializer(db, NullLogger<FlowIndexInitializer>.Instance).EnsureAsync(CancellationToken.None);
        Assert(!(await settingsRepo.GetBhytGroupCodeAsync("DN"))!.IsActive, "seed must not re-activate a code a user turned off");
        foreach (var bad in new[] { "", "  ", "D", "DNX", "D1", "Đ1", "-" })
            Assert((await Throws<DomainValidationException>(() => billingSettings.UpsertBhytGroupCodeAsync(bad, new(), admin))).Code == "BHYT_GROUP_CODE_INVALID", bad);
        var logs = await auditCol.Find(a => a.TargetEntity == "BhytGroupCode").ToListAsync();
        Assert(logs.Count == 2 && logs.Any(l => l.Action == "BHYT_GROUP_CODE_CREATED" && l.UserId == "adm-1" && l.UserRole == "Admin")
               && logs.Any(l => l.Action == "BHYT_GROUP_CODE_UPDATED" && l.UserId == "mgr-1" && l.EntityId == "DN"));
        await billingSettings.UpsertBhytGroupCodeAsync("DN", new() { IsActive = true }, manager); // restore for the next checks
        Assert((await fees.CalculateFeeAsync(req2)).NetTotalPayable == 0m);
    });

    await Check("priority discount: default 0 VND (no 10%/100k); Manager sets 5% cap 30,000 -> applied; invalid -> 400; audited", async () =>
    {
        var pri = new CalculateFeeRequestDto { PackageIds = new() { "CBC", "BIO" }, IsPriorityPatient = true };
        var d0 = await fees.CalculateFeeAsync(pri);
        Assert(d0.PriorityDiscount == 0m && d0.NetTotalPayable == 400_000m, d0.PriorityDiscount.ToString());
        Assert((await billingSettings.GetPriorityDiscountAsync()).Percent == 0m);
        Assert(FeeCalculator.PriorityDiscount(1_000_000m, FeePolicy.SeedDefault) == 0m);
        await billingSettings.SetPriorityDiscountAsync(new() { Percent = 5m, MaxAmount = 30_000m }, manager);
        var d1 = await fees.CalculateFeeAsync(pri);
        Assert(d1.PriorityDiscount == 20_000m && d1.NetTotalPayable == 380_000m, d1.PriorityDiscount.ToString());
        await billingSettings.SetPriorityDiscountAsync(new() { Percent = 10m, MaxAmount = 30_000m }, manager);
        Assert((await fees.CalculateFeeAsync(pri)).PriorityDiscount == 30_000m);
        Assert((await fees.CalculateFeeAsync(new() { PackageIds = new() { "CBC", "BIO" } })).PriorityDiscount == 0m, "non-priority patient");
        foreach (var bad in new PriorityDiscountSettingsDto[] { new() { Percent = -1m }, new() { Percent = 100.5m }, new() { Percent = 5m, MaxAmount = -1m } })
            Assert((await Throws<DomainValidationException>(() => billingSettings.SetPriorityDiscountAsync(bad, manager))).Code == "PRIORITY_DISCOUNT_INVALID");
        var logs = await auditCol.Find(a => a.Action == "PRIORITY_DISCOUNT_UPDATED").ToListAsync();
        Assert(logs.Count == 2 && logs.All(l => l.UserId == "mgr-1"));
        await billingSettings.SetPriorityDiscountAsync(new() { Percent = 0m }, manager); // back to default
        Assert((await fees.CalculateFeeAsync(pri)).PriorityDiscount == 0m);
    });
    await Check("calculate-fee uses catalog prices; unknown code -> 400 FEE_ITEM_NOT_IN_CATALOG (no 200,000/75,000 default)", async () =>
    {
        var r = await fees.CalculateFeeAsync(new CalculateFeeRequestDto { PackageIds = new() { "cbc", "BIO" }, ServiceType = "HomeCollection", InsuranceCardNumber = DN4, VoucherCode = "BMS10" });
        Assert(r.TestSubTotal == 400_000m && r.NetTotalPayable == 190_000m);
        Assert((await Throws<DomainValidationException>(() => fees.CalculateFeeAsync(new() { PackageIds = new() { "CBC", "NOPE" } }))).Code == "FEE_ITEM_NOT_IN_CATALOG");
        Assert((await Throws<DomainValidationException>(() => fees.CalculateFeeAsync(new() { IndividualTestIds = new() { "GLU-X" } }))).Code == "FEE_ITEM_NOT_IN_CATALOG");
        Assert((await Throws<DomainValidationException>(() => fees.CalculateFeeAsync(new() { PackageIds = new() { "CBC" }, InsuranceCardNumber = "XX" }))).Code == "MSG-ERR-FIN-01");
    });

    var patientAccount = "check-Patient"; // the account id the check auth handler gives the Patient role
    await db.GetCollection<Patient>("patients").InsertManyAsync(new[]
    {
        new Patient { Id = "PAT-261030-0001", FullName = "Trần Thị Bích", AccountId = patientAccount, PhoneNumber = "0912345678" },
        new Patient { Id = "PAT-261030-0002", FullName = "Lê Văn C", PhoneNumber = "0987654321" },
    });
    var orderCol = db.GetCollection<TestOrder>(TestOrderRepository.CollectionName);
    var counter = 0;
    async Task<TestOrder> NewOrder(string patientId = "PAT-261030-0001", TestOrderStatus status = TestOrderStatus.AwaitingPayment, bool deferred = false)
    {
        var o = new TestOrder
        {
            OrderNumber = $"ORD-20261030-{Interlocked.Increment(ref counter):D4}", PatientId = patientId, PatientName = "BN", DoctorId = "doc-1",
            Status = status, PaymentDeferred = deferred, Icd10Codes = new() { "E11.9" }, CreatedAt = now,
            Items = new() { new() { TestCode = "CBC", TestName = "CBC", Price = 150_000m }, new() { TestCode = "BIO", TestName = "Sinh hóa", Price = 250_000m } },
            TotalAmount = 400_000m
        };
        await orderCol.InsertOneAsync(o);
        return o;
    }
    var invoices = new InvoiceRepository(db);
    var seq = new MongoSequenceGenerator(db);
    OrderPaymentService NewPay() => new(new TestOrderRepository(db), new InvoiceRepository(db), new PatientRepository(db), seq, billingSettings, clock, NullLogger<OrderPaymentService>.Instance);
    var pay = NewPay();
    var req = new OrderPaymentRequestDto { InsuranceCardNumber = DN4, HomeCollection = true, VoucherCode = "BMS10", PaymentMethod = "Cash" };

    await Check("UC53 estimate of an order == UC55 counter payment == 190,000; order -> PaidPendingSample; receipt RCP-", async () =>
    {
        var o = await NewOrder();
        var est = await fees.EstimateOrderFeeAsync(o.OrderNumber, DN4, "BMS10", true);
        var res = await pay.PayAtCounterAsync(o.Id, req, "rec-1", "Lễ tân A");
        Assert(est!.NetTotalPayable == 190_000m && res.Fee.NetTotalPayable == 190_000m);
        Assert(res.OrderStatus == "PaidPendingSample" && res.InvoiceStatus == "Paid" && res.ReceiptNumber!.StartsWith("RCP-20261030-"));
        var inv = await invoices.GetByIdAsync(res.InvoiceId);
        Assert(inv!.TotalAmount == 190_000m && inv.OrderId == o.Id && inv.CashierId == "rec-1" && inv.InsuranceDeduction == 320_000m);
        Assert(TestOrderStatusRules.CanCollectSample((await orderRepo.GetByIdAsync(o.Id))!));
        Assert((await Throws<ConflictStateException>(() => pay.PayAtCounterAsync(o.Id, req, "rec-2", null))).Code == "ORDER_ALREADY_PAID");
    });

    await Check("20 parallel counter payments on one order -> exactly 1 Paid invoice, 19 x 409", async () =>
    {
        var o = await NewOrder();
        var res = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(async () =>
        {
            try { await NewPay().PayAtCounterAsync(o.Id, req, "rec-" + i, null); return "ok"; }
            catch (ConflictStateException) { return "409"; }
        })));
        Assert(res.Count(r => r == "ok") == 1 && res.Count(r => r == "409") == 19, string.Join(",", res));
        var paidCount = await db.GetCollection<Invoice>("invoices").CountDocumentsAsync(i => i.OrderId == o.Id && i.Status == InvoiceStatus.Paid);
        Assert(paidCount == 1);
        Console.WriteLine($"     concurrency: 20 parallel payments -> {res.Count(r => r == "ok")} paid, {paidCount} invoice");
    });

    await Check("not payable: Cancelled -> 409; bad method at counter -> 400; invalid card -> 400 before any write", async () =>
    {
        var c = await NewOrder(status: TestOrderStatus.Cancelled);
        Assert((await Throws<ConflictStateException>(() => pay.PayAtCounterAsync(c.Id, req, "rec-1", null))).Code == "ORDER_NOT_PAYABLE");
        var o = await NewOrder();
        Assert((await Throws<DomainValidationException>(() => pay.PayAtCounterAsync(o.Id, new() { PaymentMethod = "VNPay" }, "rec-1", null))).Code == "PAYMENT_METHOD_INVALID");
        Assert((await Throws<DomainValidationException>(() => pay.PayAtCounterAsync(o.Id, new() { PaymentMethod = "Cash", InsuranceCardNumber = "DN9" }, "rec-1", null))).Code == "MSG-ERR-FIN-01");
        Assert((await orderRepo.GetByIdAsync(o.Id))!.Status == TestOrderStatus.AwaitingPayment);
    });

    await Check("VNPay start: invoice Unpaid, order stays AwaitingPayment (no Paid without callback)", async () =>
    {
        var o = await NewOrder();
        var r = await pay.StartVnPayAsync(o.Id, new() { PaymentMethod = "VNPay" }, "rec-1", false);
        Assert(r.InvoiceStatus == "Unpaid" && r.OrderStatus == "AwaitingPayment");
        var stored = await orderRepo.GetByIdAsync(o.Id);
        Assert(stored!.Status == TestOrderStatus.AwaitingPayment && stored.PaidAt == null && !TestOrderStatusRules.CanCollectSample(stored));
    });

    await Check("Patient: own order VNPay ok (pending); other patient's order -> 403; Cash/POS -> 403", async () =>
    {
        var mine = await NewOrder();
        var other = await NewOrder("PAT-261030-0002");
        Assert((await pay.StartVnPayAsync(mine.Id, new() { PaymentMethod = "VNPay" }, patientAccount, true)).InvoiceStatus == "Unpaid");
        Assert((await Throws<ForbiddenAccessException>(() => pay.StartVnPayAsync(other.Id, new() { PaymentMethod = "VNPay" }, patientAccount, true))).StatusCode == 403);
        foreach (var m in new[] { "Cash", "POS" })
            Assert((await Throws<ForbiddenAccessException>(() => pay.StartVnPayAsync(mine.Id, new() { PaymentMethod = m }, patientAccount, true))).StatusCode == 403);
    });

    await Check("STAT deferred, already sampled: payment records paid_at, status stays SampleCollected", async () =>
    {
        var o = await NewOrder(status: TestOrderStatus.SampleCollected, deferred: true);
        var r = await pay.PayAtCounterAsync(o.Id, new() { PaymentMethod = "POS" }, "rec-1", null);
        Assert(r.OrderStatus == "SampleCollected" && r.Fee.NetTotalPayable == 400_000m);
        Assert((await orderRepo.GetByIdAsync(o.Id))!.PaidAt != null);
        var routineCollected = await NewOrder(status: TestOrderStatus.SampleCollected);
        Assert((await Throws<ConflictStateException>(() => pay.PayAtCounterAsync(routineCollected.Id, new() { PaymentMethod = "Cash" }, "rec-1", null))).Code == "ORDER_ALREADY_PAID");
    });

    await Check("UC55 invoice path = UC53: BMS10 capped 200,000 on 2,010,000; VNPay stays Unpaid with no fake QR; Cash marks order paid", async () =>
    {
        var svc = new PaymentService(invoices, orderRepo, clock);
        var o = await NewOrder();
        var inv = await invoices.CreateAsync(new Invoice { InvoiceCode = "INV-T-1", PatientId = "PAT-261030-0001", SubTotal = 2_010_000m, TotalAmount = 2_010_000m, OrderId = o.Id });
        var d = await svc.ApplyDiscountAsync(new ApplyDiscountRequestDto { InvoiceId = inv.Id, DiscountCode = "BMS10" });
        Assert(d.DiscountAmount == 200_000m && d.NewTotal == 1_810_000m);
        Assert(!(await svc.ApplyDiscountAsync(new ApplyDiscountRequestDto { InvoiceId = inv.Id, DiscountCode = "SPRING10" })).IsValid);
        var v = await svc.ProcessPaymentAsync(new ProcessPaymentRequestDto { InvoiceId = inv.Id, PaymentMethod = "VNPay", DiscountCode = "BMS10" }, "pat", null);
        Assert(v.Status == "Unpaid" && v.QrCodePayload == null && v.TotalAmount == 1_810_000m);
        var c = await svc.ProcessPaymentAsync(new ProcessPaymentRequestDto { InvoiceId = inv.Id, PaymentMethod = "Cash" }, "rec-1", "LT");
        Assert(c.Status == "Paid");
        Assert((await orderRepo.GetByIdAsync(o.Id))!.Status == TestOrderStatus.PaidPendingSample);
        await Throws<ConflictStateException>(() => svc.ProcessPaymentAsync(new ProcessPaymentRequestDto { InvoiceId = inv.Id, PaymentMethod = "Cash" }, "rec-1", "LT"));
    });

    await Check("HTTP: calculate-fee 401 anon, 403 Manager/Lab/Doctor/Admin/Patient, 200 Receptionist, bad card 400 MSG-ERR-FIN-01", async () =>
    {
        await using var app = await StartAsync(s =>
        {
            s.AddScoped<ICalculateFeeService>(_ => fees);
            s.AddScoped<IOrderPaymentService>(_ => NewPay());
        });
        using var http = Client(app);
        var body = new { packageIds = new[] { "CBC", "BIO" }, serviceType = "HomeCollection", insuranceCardNumber = DN4, voucherCode = "BMS10" };
        Assert((await http.PostAsJsonAsync("/api/billing/calculate-fee", body)).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Manager", "Lab", "Doctor", "Admin", "Patient" })
            Assert((await As(http, role).PostAsJsonAsync("/api/billing/calculate-fee", body)).StatusCode == HttpStatusCode.Forbidden, role);
        var ok = await As(http, "Receptionist").PostAsJsonAsync("/api/billing/calculate-fee", body);
        Assert(ok.StatusCode == HttpStatusCode.OK && JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement.GetProperty("data").GetProperty("netTotalPayable").GetDecimal() == 190_000m);
        var bad = await As(http, "Receptionist").PostAsJsonAsync("/api/billing/calculate-fee", new { packageIds = new[] { "CBC" }, insuranceCardNumber = "DN4" });
        var t = await bad.Content.ReadAsStringAsync();
        Assert(bad.StatusCode == HttpStatusCode.BadRequest && t.Contains("MSG-ERR-FIN-01") && !t.Contains("   at "), t);
        await app.StopAsync();
    });

    await Check("HTTP: order pay 403 Manager/Patient/Lab/Doctor/Admin, 200 Receptionist then 409; vnpay Patient other's 403, Cash 403, own 200", async () =>
    {
        await using var app = await StartAsync(s =>
        {
            s.AddScoped<ICalculateFeeService>(_ => fees);
            s.AddScoped<IOrderPaymentService>(_ => NewPay());
        });
        using var http = Client(app);
        var o = await NewOrder();
        var other = await NewOrder("PAT-261030-0002");
        var cash = new { paymentMethod = "Cash" };
        Assert((await http.PostAsJsonAsync($"/api/billing/orders/{o.Id}/pay", cash)).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Manager", "Patient", "Lab", "Doctor", "Admin" })
            Assert((await As(http, role).PostAsJsonAsync($"/api/billing/orders/{o.Id}/pay", cash)).StatusCode == HttpStatusCode.Forbidden, role);
        Assert((await As(http, "Manager").PostAsJsonAsync($"/api/billing/orders/{o.Id}/quote", cash)).StatusCode == HttpStatusCode.Forbidden);
        Assert((await As(http, "Patient").PostAsJsonAsync($"/api/billing/orders/{other.Id}/vnpay", new { paymentMethod = "VNPay" })).StatusCode == HttpStatusCode.Forbidden);
        Assert((await As(http, "Patient").PostAsJsonAsync($"/api/billing/orders/{o.Id}/vnpay", new { paymentMethod = "Cash" })).StatusCode == HttpStatusCode.Forbidden);
        Assert((await As(http, "Patient").PostAsJsonAsync($"/api/billing/orders/{o.Id}/vnpay", new { paymentMethod = "VNPay" })).StatusCode == HttpStatusCode.OK);
        var paid = await As(http, "Receptionist").PostAsJsonAsync($"/api/billing/orders/{o.Id}/pay", cash);
        Assert(paid.StatusCode == HttpStatusCode.OK, await paid.Content.ReadAsStringAsync());
        Assert((await As(http, "Receptionist").PostAsJsonAsync($"/api/billing/orders/{o.Id}/pay", cash)).StatusCode == HttpStatusCode.Conflict);
        await app.StopAsync();
    });
    await Check("HTTP settings: 401 anon; bhyt-groups GET/PUT Admin+Manager 200, Lab/Doctor/Receptionist/Patient 403; priority-discount PUT Manager 200, Admin/Receptionist 403", async () =>
    {
        await using var app = await StartAsync(s => s.AddScoped<IBillingSettingsService>(_ => billingSettings));
        using var http = Client(app);
        var put = new { description = "x", isActive = true };
        Assert((await http.GetAsync("/api/billing/settings/bhyt-groups")).StatusCode == HttpStatusCode.Unauthorized);
        Assert((await http.PutAsJsonAsync("/api/billing/settings/bhyt-groups/HN", put)).StatusCode == HttpStatusCode.Unauthorized);
        foreach (var role in new[] { "Lab", "Doctor", "Receptionist", "Patient" })
        {
            Assert((await As(http, role).GetAsync("/api/billing/settings/bhyt-groups")).StatusCode == HttpStatusCode.Forbidden, role);
            Assert((await As(http, role).PutAsJsonAsync("/api/billing/settings/bhyt-groups/HN", put)).StatusCode == HttpStatusCode.Forbidden, role);
        }
        foreach (var role in new[] { "Admin", "Manager" })
        {
            Assert((await As(http, role).GetAsync("/api/billing/settings/bhyt-groups")).StatusCode == HttpStatusCode.OK, role);
            var r = await As(http, role).PutAsJsonAsync("/api/billing/settings/bhyt-groups/%20hn%20", put);
            Assert(r.StatusCode == HttpStatusCode.OK && (await r.Content.ReadAsStringAsync()).Contains("\"code\":\"HN\""), role);
        }
        var bad = await As(http, "Admin").PutAsJsonAsync("/api/billing/settings/bhyt-groups/H1", put);
        Assert(bad.StatusCode == HttpStatusCode.BadRequest && (await bad.Content.ReadAsStringAsync()).Contains("BHYT_GROUP_CODE_INVALID"));
        var pd = new { percent = 0, maxAmount = (decimal?)null };
        foreach (var role in new[] { "Admin", "Receptionist", "Lab", "Doctor", "Patient" })
            Assert((await As(http, role).PutAsJsonAsync("/api/billing/settings/priority-discount", pd)).StatusCode == HttpStatusCode.Forbidden, role);
        Assert((await As(http, "Manager").PutAsJsonAsync("/api/billing/settings/priority-discount", pd)).StatusCode == HttpStatusCode.OK);
        Assert((await As(http, "Manager").GetAsync("/api/billing/settings/priority-discount")).StatusCode == HttpStatusCode.OK);
        Assert((await As(http, "Admin").GetAsync("/api/billing/settings/priority-discount")).StatusCode == HttpStatusCode.Forbidden);
        var badPd = await As(http, "Manager").PutAsJsonAsync("/api/billing/settings/priority-discount", new { percent = 101 });
        Assert(badPd.StatusCode == HttpStatusCode.BadRequest && (await badPd.Content.ReadAsStringAsync()).Contains("PRIORITY_DISCOUNT_INVALID"));
        var audit = await auditCol.Find(a => a.EntityId == "HN").ToListAsync();
        Assert(audit.Count == 2 && audit.Select(a => a.UserId).OrderBy(x => x).SequenceEqual(new[] { "check-Admin", "check-Manager" }), string.Join(",", audit.Select(a => a.UserId)));
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
    if (databaseName.StartsWith("BMS_UC55_Checks_", StringComparison.Ordinal)) await client.DropDatabaseAsync(databaseName);
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
    builder.Services.AddControllers().AddApplicationPart(typeof(FeeCalculationController).Assembly);
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

