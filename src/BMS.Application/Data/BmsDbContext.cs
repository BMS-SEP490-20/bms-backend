using BMS.Core.Entities;

namespace BMS.Infrastructure.Data;

public class BmsDbContext
{
    // Thread-safe in-memory collections for fast startup & testing
    public List<User> Users { get; } = new();
    public List<Sample> Samples { get; } = new();
    public List<AppointmentNotification> AppointmentNotifications { get; } = new();
    public List<QueueTicket> QueueTickets { get; } = new();
    public List<TestOrder> TestOrders { get; } = new();
    public List<SampleCollectionList> SampleCollectionLists { get; } = new();
    public List<TestReport> TestReports { get; } = new();
    public List<TestNote> TestNotes { get; } = new();
    public List<Invoice> Invoices { get; } = new();
    public List<IotDeviceThreshold> IotThresholds { get; } = new();
    public List<IotAlertRecipient> IotAlertRecipients { get; } = new();
    public List<TestResultVerification> TestVerifications { get; } = new();
    public List<SampleOriginTrace> SampleOriginTraces { get; } = new();

    public BmsDbContext()
    {
        SeedInitialData();
    }

    private void SeedInitialData()
    {
        // Seed default users for testing role dashboards
        Users.AddRange(new[]
        {
            new User
            {
                Id = "USR-ADMIN-01",
                Username = "admin",
                Email = "admin@bms.com",
                FullName = "Hệ thống Quản Trị Viên",
                Role = UserRole.Administrator,
                PasswordHash = HashPassword("Admin@123"),
                EmailConfirmed = true,
                IsActive = true
            },
            new User
            {
                Id = "USR-MGR-01",
                Username = "manager",
                Email = "manager@bms.com",
                FullName = "Trần Quản Lý Lab",
                Role = UserRole.Manager,
                PasswordHash = HashPassword("Manager@123"),
                EmailConfirmed = true,
                IsActive = true
            },
            new User
            {
                Id = "USR-DOC-01",
                Username = "doctor",
                Email = "doctor@bms.com",
                FullName = "BS. Nguyễn Văn An",
                Role = UserRole.Doctor,
                PasswordHash = HashPassword("Doctor@123"),
                EmailConfirmed = true,
                IsActive = true
            },
            new User
            {
                Id = "USR-LAB-01",
                Username = "labuser",
                Email = "labuser@bms.com",
                FullName = "KTV. Lê Thị Bình",
                Role = UserRole.LaboratoryUser,
                PasswordHash = HashPassword("LabUser@123"),
                EmailConfirmed = true,
                IsActive = true
            },
            new User
            {
                Id = "USR-REC-01",
                Username = "receptionist",
                Email = "receptionist@bms.com",
                FullName = "Lễ Tân Mai Hoa",
                Role = UserRole.Receptionist,
                PasswordHash = HashPassword("Recep@123"),
                EmailConfirmed = true,
                IsActive = true
            },
            new User
            {
                Id = "USR-PAT-01",
                Username = "patient",
                Email = "patient@bms.com",
                FullName = "Bệnh Nhân Võ Gia Huy",
                PhoneNumber = "0901234567",
                Role = UserRole.Patient,
                PasswordHash = HashPassword("Admin@123"),
                EmailConfirmed = true,
                IsActive = true
            },
            new User
            {
                Id = "USR-ADMIN-BMS",
                Username = "admin_bms",
                Email = "admin@bms.health.vn",
                FullName = "Nguyễn Quản Trị",
                Role = UserRole.Administrator,
                PasswordHash = HashPassword("Admin@123"),
                EmailConfirmed = true,
                IsActive = true
            },
            new User
            {
                Id = "USR-DOC-MINH",
                Username = "dr_nguyenminh",
                Email = "minh.nguyen@bms.health.vn",
                FullName = "BS. CKI Nguyễn Văn Minh",
                Role = UserRole.Doctor,
                PasswordHash = HashPassword("Admin@123"),
                EmailConfirmed = true,
                IsActive = true
            },
            new User
            {
                Id = "USR-LAB-HUNG",
                Username = "tech_quochung",
                Email = "hung.pham@bms.health.vn",
                FullName = "KTV. Phạm Quốc Hưng",
                Role = UserRole.LaboratoryUser,
                PasswordHash = HashPassword("Admin@123"),
                EmailConfirmed = true,
                IsActive = true
            },
            new User
            {
                Id = "USR-REC-LANANH",
                Username = "recep_lananh",
                Email = "anh.lan@bms.health.vn",
                FullName = "Lễ tân Đỗ Lan Anh",
                Role = UserRole.Receptionist,
                PasswordHash = HashPassword("Admin@123"),
                EmailConfirmed = true,
                IsActive = true
            },
            new User
            {
                Id = "USR-PAT-AN",
                Username = "patient_tranvanan",
                Email = "an.tran@gmail.com",
                FullName = "Trần Văn An",
                PhoneNumber = "0909000001",
                Role = UserRole.Patient,
                PasswordHash = HashPassword("Admin@123"),
                EmailConfirmed = true,
                IsActive = true
            }
        });

        // Seed initial laboratory samples for Lab Queue testing
        var now = DateTime.UtcNow;
        Samples.AddRange(new[]
        {
            new Sample
            {
                Id = "SMP-001",
                Barcode = "BC-20261011-0001",
                TestOrderId = "ORD-101",
                PatientId = "USR-PAT-01",
                PatientName = "Võ Gia Huy",
                PatientGender = "Nam",
                PatientAge = 22,
                TestPackageName = "Tổng phân tích tế bào máu ngoại vi (CBC)",
                SampleType = "Blood",
                Status = SampleStatus.Received,
                Priority = SamplePriority.Urgent,
                LabRoom = "Phòng Lab Huyết Học 101",
                CollectedAt = now.AddMinutes(-45),
                ReceivedAt = now.AddMinutes(-20),
                Notes = "Khách hàng cần kết quả sớm trước 10h"
            },
            new Sample
            {
                Id = "SMP-002",
                Barcode = "BC-20261011-0002",
                TestOrderId = "ORD-102",
                PatientId = "USR-PAT-02",
                PatientName = "Nguyễn Thị Mai",
                PatientGender = "Nữ",
                PatientAge = 35,
                TestPackageName = "Sinh hóa máu cơ bản (Glucose, Ure, Creatinine)",
                SampleType = "Serum",
                Status = SampleStatus.Processing,
                Priority = SamplePriority.Normal,
                LabRoom = "Phòng Lab Sinh Hóa 102",
                AssignedTechnicianName = "KTV. Lê Thị Bình",
                CollectedAt = now.AddHours(-1),
                ReceivedAt = now.AddMinutes(-40),
                ProcessedAt = now.AddMinutes(-10)
            },
            new Sample
            {
                Id = "SMP-003",
                Barcode = "BC-20261011-0003",
                TestOrderId = "ORD-103",
                PatientId = "USR-PAT-03",
                PatientName = "Trần Hoàng Long",
                PatientGender = "Nam",
                PatientAge = 48,
                TestPackageName = "Bộ xét nghiệm Men Gan (AST, ALT, GGT)",
                SampleType = "Serum",
                Status = SampleStatus.Received,
                Priority = SamplePriority.Normal,
                LabRoom = "Phòng Lab Sinh Hóa 102",
                CollectedAt = now.AddMinutes(-30),
                ReceivedAt = now.AddMinutes(-15)
            },
            new Sample
            {
                Id = "SMP-004",
                Barcode = "BC-20261011-0004",
                TestOrderId = "ORD-104",
                PatientId = "USR-PAT-04",
                PatientName = "Đặng Thùy Dung",
                PatientGender = "Nữ",
                PatientAge = 28,
                TestPackageName = "Đông máu toàn bộ (PT, APTT, Fibrinogen)",
                SampleType = "Plasma",
                Status = SampleStatus.RerunRequested,
                Priority = SamplePriority.Stat,
                LabRoom = "Phòng Lab Huyết Học 101",
                RerunReason = "Chỉ số APTT bất thường nghi đông dây máu, cần chạy lại mẫu kép",
                CollectedAt = now.AddHours(-2),
                ReceivedAt = now.AddHours(-1)
            },
            new Sample
            {
                Id = "SMP-005",
                Barcode = "BC-20261011-0005",
                TestOrderId = "ORD-105",
                PatientId = "USR-PAT-05",
                PatientName = "Phạm Quốc Tuấn",
                PatientGender = "Nam",
                PatientAge = 55,
                TestPackageName = "Định lượng Lipid máu (Cholesterol, Triglyceride)",
                SampleType = "Serum",
                Status = SampleStatus.Completed,
                Priority = SamplePriority.Normal,
                LabRoom = "Phòng Lab Sinh Hóa 102",
                AssignedTechnicianName = "KTV. Lê Thị Bình",
                CollectedAt = now.AddHours(-3),
                ReceivedAt = now.AddHours(-2),
                CompletedAt = now.AddMinutes(-20)
            }
        });

        // Phlebotomy collection lists are dynamically persisted to MongoDB or loaded at runtime

        // Seed default IoT thresholds (UC67)
        IotThresholds.AddRange(new[]
        {
            new IotDeviceThreshold { DeviceId = "TEMP-001", DeviceName = "Fridge Temperature Sensor 1", Location = "Fridge R-01", SensorType = "Temperature", Unit = "°C", LowAlert = 2.0, HighAlert = 8.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "TEMP-002", DeviceName = "Fridge Temperature Sensor 2", Location = "Fridge R-02", SensorType = "Temperature", Unit = "°C", LowAlert = 2.0, HighAlert = 8.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "TEMP-003", DeviceName = "Freezer Temperature Sensor", Location = "Freezer F-01", SensorType = "Temperature", Unit = "°C", LowAlert = -25.0, HighAlert = -15.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "TEMP-004", DeviceName = "Ambient Lab Temperature", Location = "Lab Room 01", SensorType = "Temperature", Unit = "°C", LowAlert = 18.0, HighAlert = 26.0, IntervalSeconds = 120, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "HUM-001", DeviceName = "Cold Storage Humidity", Location = "Cold Storage", SensorType = "Humidity", Unit = "%", LowAlert = 30.0, HighAlert = 60.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "HUM-002", DeviceName = "Lab Room Humidity", Location = "Lab Room 01", SensorType = "Humidity", Unit = "%", LowAlert = 30.0, HighAlert = 60.0, IntervalSeconds = 120, Status = "Active" }
        });

        // Seed default IoT alert recipients (UC67)
        IotAlertRecipients.AddRange(new[]
        {
            new IotAlertRecipient { FullName = "Trần Minh Tuấn", Role = "Manager", Email = "tuan.tran@bms.health", Phone = "+84 901 234 567", Channels = new() { "Push", "Email" } },
            new IotAlertRecipient { FullName = "Võ Minh Khoa", Role = "Cold storage lead", Email = "khoa.vo@bms.health", Phone = "+84 902 345 678", Channels = new() { "Push", "Email", "SMS" } },
            new IotAlertRecipient { FullName = "Phạm Thu Dung", Role = "Lab lead", Email = "dung.pham@bms.health", Phone = "+84 903 456 789", Channels = new() { "Push", "Email" } }
        });

        // Seed initial technical test verification checklist (UC72)
        TestVerifications.Add(new TestResultVerification
        {
            Id = "VERIF-261006-0089",
            SpecimenId = "SPC-261006-A1-0089",
            OrderId = "ORD-261006-0187",
            PackageName = "Lipid Profile",
            PatientId = "PT-2026-004523",
            PatientName = "Nguyễn Văn An",
            Status = "PendingVerification",
            CreatedAt = now.AddMinutes(-45),
            Checklist = new List<VerificationCheckItem>
            {
                new VerificationCheckItem { CheckName = "Patient identity confirmed", Status = "OK", StatusType = "ok", Details = "Nguyễn Văn An matches order ORD-261006-0187" },
                new VerificationCheckItem { CheckName = "Analyzer QC passed", Status = "Flag", StatusType = "warn", Details = "QCF-1142 active (1-3s) on BC-01 — under review" },
                new VerificationCheckItem { CheckName = "Reagent lot valid", Status = "OK", StatusType = "ok", Details = "LOT-7721 · Expires 31 Dec 2026" },
                new VerificationCheckItem { CheckName = "Delta check vs. baseline", Status = "Flag", StatusType = "warn", Details = "LDL +0.42 mmol/L vs. Jun 2026 — rerun confirmed same value" },
                new VerificationCheckItem { CheckName = "Panic / critical values", Status = "None", StatusType = "ok", Details = "No critical values detected in this specimen" },
                new VerificationCheckItem { CheckName = "Reference intervals applied", Status = "OK", StatusType = "ok", Details = "Adult male 18-65y profile · Age-specific" },
                new VerificationCheckItem { CheckName = "Technical notes complete", Status = "OK", StatusType = "ok", Details = "3 notes attached by Phạm Thu Dung" },
                new VerificationCheckItem { CheckName = "Rerun results", Status = "Confirmed", StatusType = "ok", Details = "LDL 3.95 on repeat — matches original" }
            }
        });

        // Seed initial sample origin trace and custody chain (UC77)
        SampleOriginTraces.Add(new SampleOriginTrace
        {
            Id = "TRACE-261006-0089",
            SpecimenId = "SPC-261006-A1-0089",
            OrderId = "ORD-261006-0187",
            PatientId = "PT-004523",
            PatientName = "Nguyễn Văn An",
            CreatedAt = now.AddMinutes(-40),
            Custody = new ChainOfCustody
            {
                Integrity = "Unbroken",
                BarcodeScansCount = 7,
                TransportDuration = "12 min (Room 02 → Chem bench)",
                AnalyzerModel = "BC-01 · Calibrated 07:30",
                ReagentLot = "LOT-7721 · Exp 31 Dec 2026",
                QcStatus = "QCF-1142",
                RerunCount = 1,
                NotesCount = 3
            },
            Nodes = new List<TraceNode>
            {
                new TraceNode { NodeType = "Patient", Title = "Nguyễn Văn An", Subtitle = "PT-004523", Code = "PT-004523", Badge = "Patient", BadgeType = "patient" },
                new TraceNode { NodeType = "Doctor", Title = "Dr. Trần Thị Bình", Subtitle = "Internal Medicine", Code = "DOC-01", Badge = "Doctor", BadgeType = "doctor" },
                new TraceNode { NodeType = "Order", Title = "ORD-261006-0187", Subtitle = "Lipid + Glucose", Code = "ORD-261006-0187", Badge = "Order", BadgeType = "order" },
                new TraceNode { NodeType = "Collection", Title = "Phạm Thu Dung", Subtitle = "Lab Room 02 · 08:05", Code = "COL-01", Badge = "Collection", BadgeType = "warn" },
                new TraceNode { NodeType = "Specimen", Title = "SPC-0089", Subtitle = "SST 4mL", Code = "SPC-261006-A1-0089", Badge = "Specimen", BadgeType = "err" },
                new TraceNode { NodeType = "Analyzer", Title = "BC-01", Subtitle = "LOT-7721 · BATCH-02", Code = "BC-01", Badge = "Analyzer", BadgeType = "warn" },
                new TraceNode { NodeType = "Result", Title = "5 values", Subtitle = "HH LDL 3.95", Code = "RES-01", Badge = "Result", BadgeType = "ok" },
                new TraceNode { NodeType = "Status", Title = "Awaiting entry", Subtitle = "Rerun confirmed", Code = "VERIFIED", Badge = "Status", BadgeType = "patient" }
            }
        });
    }

    public static string HashPassword(string password)
    {
        // Simple salt-hash for demonstrative internal store; can be swapped with BCrypt
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(password + "_BMS_SALT_2026");
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public static bool VerifyPassword(string password, string storedHash)
    {
        return HashPassword(password) == storedHash;
    }
}
