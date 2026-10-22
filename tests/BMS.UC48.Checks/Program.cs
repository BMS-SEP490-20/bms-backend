using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BMS.Application.DTOs;
using BMS.Application.Services;
using BMS.Core.Entities;
using MongoDB.Driver;

Console.WriteLine("=================================================");
Console.WriteLine("STARTING UC48 TRACK TEST PROGRESS VERIFICATION CHECKS");
Console.WriteLine("=================================================");

var client = new MongoClient("mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=3000");
var databaseName = "BMS_UC48_Checks_" + Guid.NewGuid().ToString("N");
var db = client.GetDatabase(databaseName);
var passed = 0;

try
{
    var orders = db.GetCollection<TestOrder>("test_orders");
    var samples = db.GetCollection<Sample>("blood_samples");
    var results = db.GetCollection<TestResult>("test_results");

    var now = DateTime.UtcNow;

    // Fixture 1: Standard Completed Order
    var order1 = new TestOrder
    {
        Id = "ORD-001",
        OrderNumber = "ORD-2026-0001",
        PatientId = "PAT-001",
        PatientName = "Nguyễn Tấn Phát",
        PatientGender = "Nam",
        PatientAge = 25,
        Priority = SamplePriority.Normal,
        Status = TestOrderStatus.Completed,
        CreatedAt = now.AddMinutes(-90),
        Items = new List<TestOrderItem>
        {
            new TestOrderItem { TestCode = "CBC", TestName = "Tổng phân tích tế bào máu" }
        }
    };
    await orders.InsertOneAsync(order1);

    var sample1 = new Sample
    {
        Id = "SMP-001",
        Barcode = "BC-2026-0001",
        TestOrderId = "ORD-001",
        PatientId = "PAT-001",
        PatientName = "Nguyễn Tấn Phát",
        CollectedAt = now.AddMinutes(-75),
        ReceivedAt = now.AddMinutes(-60),
        ProcessedAt = now.AddMinutes(-30),
        CompletedAt = now.AddMinutes(-10),
        Status = SampleStatus.Completed
    };
    await samples.InsertOneAsync(sample1);

    // Fixture 2: Urgent / STAT Order In Progress
    var order2 = new TestOrder
    {
        Id = "ORD-002",
        OrderNumber = "ORD-2026-STAT",
        PatientId = "PAT-002",
        PatientName = "Trần Thị Hoa",
        PatientGender = "Nữ",
        PatientAge = 40,
        Priority = SamplePriority.Stat,
        Status = TestOrderStatus.SampleCollected,
        CreatedAt = now.AddMinutes(-15)
    };
    await orders.InsertOneAsync(order2);

    var sample2 = new Sample
    {
        Id = "SMP-002",
        Barcode = "BC-2026-STAT",
        TestOrderId = "ORD-002",
        PatientId = "PAT-002",
        PatientName = "Trần Thị Hoa",
        CollectedAt = now.AddMinutes(-10),
        Status = SampleStatus.Collected
    };
    await samples.InsertOneAsync(sample2);

    // Fixture 3: Rejected Sample
    var order3 = new TestOrder
    {
        Id = "ORD-003",
        OrderNumber = "ORD-2026-REJ",
        PatientId = "PAT-003",
        PatientName = "Lê Văn Cường",
        Status = TestOrderStatus.Ordered,
        CreatedAt = now.AddMinutes(-40)
    };
    await orders.InsertOneAsync(order3);

    var sample3 = new Sample
    {
        Id = "SMP-003",
        Barcode = "BC-2026-REJ",
        TestOrderId = "ORD-003",
        PatientId = "PAT-003",
        PatientName = "Lê Văn Cường",
        Status = SampleStatus.Rejected,
        RejectionReason = "REJ-01: Mẫu bị huyết tán nặng (Hemolysis)"
    };
    await samples.InsertOneAsync(sample3);

    var service = new TrackTestProgressService(db);

    // Check 1: Public lookup masks patient name
    var pubResult1 = await service.GetTrackingAsync("ORD-2026-0001", isPublicQuery: true);
    if (pubResult1 != null && pubResult1.PatientName.Contains("*") && pubResult1.PatientName.StartsWith("Nguyễn"))
    {
        Console.WriteLine("[PASS] Check 1: Public lookup masks patient name correctly.");
        passed++;
    }
    else
    {
        Console.WriteLine("[FAIL] Check 1: Patient name masking failed. Got: " + pubResult1?.PatientName);
    }

    // Check 2: Staff lookup returns unmasked name
    var staffResult1 = await service.GetTrackingAsync("ORD-2026-0001", isPublicQuery: false);
    if (staffResult1 != null && staffResult1.PatientName == "Nguyễn Tấn Phát")
    {
        Console.WriteLine("[PASS] Check 2: Staff lookup retains unmasked patient name.");
        passed++;
    }
    else
    {
        Console.WriteLine("[FAIL] Check 2: Unmasked lookup failed.");
    }

    // Check 3: Completed order has all 6 steps completed
    if (staffResult1 != null && staffResult1.Steps.Count == 6 && staffResult1.IsCompleted && staffResult1.CurrentStepNumber == 6)
    {
        Console.WriteLine("[PASS] Check 3: Completed order has 6 steps and completion flag.");
        passed++;
    }
    else
    {
        Console.WriteLine("[FAIL] Check 3: Completed order steps check failed.");
    }

    // Check 4: Lookup by Sample Barcode works
    var barcodeResult = await service.GetTrackingAsync("BC-2026-STAT", isPublicQuery: false);
    if (barcodeResult != null && barcodeResult.OrderNumber == "ORD-2026-STAT")
    {
        Console.WriteLine("[PASS] Check 4: Lookup by Barcode successfully resolved TestOrder.");
        passed++;
    }
    else
    {
        Console.WriteLine("[FAIL] Check 4: Lookup by Barcode failed.");
    }

    // Check 5: STAT order sets 60 min ETA duration
    if (barcodeResult != null && barcodeResult.Priority == "Stat" && Math.Abs((barcodeResult.EstimatedCompletionTime - order2.CreatedAt.AddMinutes(60)).TotalSeconds) < 2)
    {
        Console.WriteLine("[PASS] Check 5: STAT order correctly computes 60 min ETA duration.");
        passed++;
    }
    else
    {
        Console.WriteLine("[FAIL] Check 5: STAT order ETA check failed. Got: " + barcodeResult?.EstimatedCompletionTime + " vs " + order2.CreatedAt.AddMinutes(60));
    }

    // Check 6: Rejected sample reflects Step 3 Rejected status
    var rejResult = await service.GetTrackingAsync("ORD-2026-REJ", isPublicQuery: false);
    if (rejResult != null && rejResult.IsRejected && rejResult.Steps[2].Status == TrackingStepStatus.Rejected)
    {
        Console.WriteLine("[PASS] Check 6: Sample rejection correctly marked in Step 3.");
        passed++;
    }
    else
    {
        Console.WriteLine("[FAIL] Check 6: Rejection status check failed.");
    }

    // Check 7: Non-existent identifier returns null
    var notFound = await service.GetTrackingAsync("NON-EXISTENT-XYZ");
    if (notFound == null)
    {
        Console.WriteLine("[PASS] Check 7: Non-existent order returns null.");
        passed++;
    }
    else
    {
        Console.WriteLine("[FAIL] Check 7: Expected null for non-existent order.");
    }

    // Check 8: Patient active trackings queries only specified patient
    var patientTrackings = await service.GetPatientActiveTrackingsAsync("PAT-001");
    if (patientTrackings.Count == 1 && patientTrackings[0].OrderNumber == "ORD-2026-0001")
    {
        Console.WriteLine("[PASS] Check 8: Patient active trackings query returns correct patient records.");
        passed++;
    }
    else
    {
        Console.WriteLine("[FAIL] Check 8: Patient active trackings check failed.");
    }

    Console.WriteLine($"\n=================================================");
    Console.WriteLine($"TOTAL PASSED: {passed} / 8 CHECKS");
    Console.WriteLine("=================================================");
}
catch (Exception ex)
{
    Console.WriteLine("FATAL ERROR: " + ex);
}
finally
{
    try { await client.DropDatabaseAsync(databaseName); } catch { }
}
