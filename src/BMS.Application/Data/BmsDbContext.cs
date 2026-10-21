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
    public List<MedicalAssessment> MedicalAssessments { get; } = new();

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
                FullName = "HÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡ thÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœng QuÃƒÂ¡Ã‚ÂºÃ‚Â£n TrÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ ViÃƒÆ’Ã‚Âªn",
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
                FullName = "TrÃƒÂ¡Ã‚ÂºÃ‚Â§n QuÃƒÂ¡Ã‚ÂºÃ‚Â£n LÃƒÆ’Ã‚Â½ Lab",
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
                FullName = "BS. NguyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¦n VÃƒâ€žÃ†â€™n An",
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
                FullName = "KTV. LÃƒÆ’Ã‚Âª ThÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ BÃƒÆ’Ã‚Â¬nh",
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
                FullName = "LÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¦ TÃƒÆ’Ã‚Â¢n Mai Hoa",
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
                FullName = "BÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡nh NhÃƒÆ’Ã‚Â¢n VÃƒÆ’Ã‚Âµ Gia Huy",
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
                FullName = "NguyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¦n QuÃƒÂ¡Ã‚ÂºÃ‚Â£n TrÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹",
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
                FullName = "BS. CKI NguyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¦n VÃƒâ€žÃ†â€™n Minh",
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
                FullName = "KTV. PhÃƒÂ¡Ã‚ÂºÃ‚Â¡m QuÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœc HÃƒâ€ Ã‚Â°ng",
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
                FullName = "LÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¦ tÃƒÆ’Ã‚Â¢n Ãƒâ€žÃ‚ÂÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ Lan Anh",
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
                FullName = "TrÃƒÂ¡Ã‚ÂºÃ‚Â§n VÃƒâ€žÃ†â€™n An",
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
                PatientName = "VÃƒÆ’Ã‚Âµ Gia Huy",
                PatientGender = "Nam",
                PatientAge = 22,
                TestPackageName = "TÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¢ng phÃƒÆ’Ã‚Â¢n tÃƒÆ’Ã‚Â­ch tÃƒÂ¡Ã‚ÂºÃ‚Â¿ bÃƒÆ’Ã‚Â o mÃƒÆ’Ã‚Â¡u ngoÃƒÂ¡Ã‚ÂºÃ‚Â¡i vi (CBC)",
                SampleType = "Blood",
                Status = SampleStatus.Received,
                Priority = SamplePriority.Urgent,
                LabRoom = "PhÃƒÆ’Ã‚Â²ng Lab HuyÃƒÂ¡Ã‚ÂºÃ‚Â¿t HÃƒÂ¡Ã‚Â»Ã‚Âc 101",
                CollectedAt = now.AddMinutes(-45),
                ReceivedAt = now.AddMinutes(-20),
                Notes = "KhÃƒÆ’Ã‚Â¡ch hÃƒÆ’Ã‚Â ng cÃƒÂ¡Ã‚ÂºÃ‚Â§n kÃƒÂ¡Ã‚ÂºÃ‚Â¿t quÃƒÂ¡Ã‚ÂºÃ‚Â£ sÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºm trÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc 10h"
            },
            new Sample
            {
                Id = "SMP-002",
                Barcode = "BC-20261011-0002",
                TestOrderId = "ORD-102",
                PatientId = "USR-PAT-02",
                PatientName = "NguyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¦n ThÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ Mai",
                PatientGender = "NÃƒÂ¡Ã‚Â»Ã‚Â¯",
                PatientAge = 35,
                TestPackageName = "Sinh hÃƒÆ’Ã‚Â³a mÃƒÆ’Ã‚Â¡u cÃƒâ€ Ã‚Â¡ bÃƒÂ¡Ã‚ÂºÃ‚Â£n (Glucose, Ure, Creatinine)",
                SampleType = "Serum",
                Status = SampleStatus.Processing,
                Priority = SamplePriority.Normal,
                LabRoom = "PhÃƒÆ’Ã‚Â²ng Lab Sinh HÃƒÆ’Ã‚Â³a 102",
                AssignedTechnicianName = "KTV. LÃƒÆ’Ã‚Âª ThÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ BÃƒÆ’Ã‚Â¬nh",
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
                PatientName = "TrÃƒÂ¡Ã‚ÂºÃ‚Â§n HoÃƒÆ’Ã‚Â ng Long",
                PatientGender = "Nam",
                PatientAge = 48,
                TestPackageName = "BÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ xÃƒÆ’Ã‚Â©t nghiÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡m Men Gan (AST, ALT, GGT)",
                SampleType = "Serum",
                Status = SampleStatus.Received,
                Priority = SamplePriority.Normal,
                LabRoom = "PhÃƒÆ’Ã‚Â²ng Lab Sinh HÃƒÆ’Ã‚Â³a 102",
                CollectedAt = now.AddMinutes(-30),
                ReceivedAt = now.AddMinutes(-15)
            },
            new Sample
            {
                Id = "SMP-004",
                Barcode = "BC-20261011-0004",
                TestOrderId = "ORD-104",
                PatientId = "USR-PAT-04",
                PatientName = "Ãƒâ€žÃ‚ÂÃƒÂ¡Ã‚ÂºÃ‚Â·ng ThÃƒÆ’Ã‚Â¹y Dung",
                PatientGender = "NÃƒÂ¡Ã‚Â»Ã‚Â¯",
                PatientAge = 28,
                TestPackageName = "Ãƒâ€žÃ‚ÂÃƒÆ’Ã‚Â´ng mÃƒÆ’Ã‚Â¡u toÃƒÆ’Ã‚Â n bÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ (PT, APTT, Fibrinogen)",
                SampleType = "Plasma",
                Status = SampleStatus.RerunRequested,
                Priority = SamplePriority.Stat,
                LabRoom = "PhÃƒÆ’Ã‚Â²ng Lab HuyÃƒÂ¡Ã‚ÂºÃ‚Â¿t HÃƒÂ¡Ã‚Â»Ã‚Âc 101",
                RerunReason = "ChÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° sÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœ APTT bÃƒÂ¡Ã‚ÂºÃ‚Â¥t thÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Âng nghi Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â´ng dÃƒÆ’Ã‚Â¢y mÃƒÆ’Ã‚Â¡u, cÃƒÂ¡Ã‚ÂºÃ‚Â§n chÃƒÂ¡Ã‚ÂºÃ‚Â¡y lÃƒÂ¡Ã‚ÂºÃ‚Â¡i mÃƒÂ¡Ã‚ÂºÃ‚Â«u kÃƒÆ’Ã‚Â©p",
                CollectedAt = now.AddHours(-2),
                ReceivedAt = now.AddHours(-1)
            },
            new Sample
            {
                Id = "SMP-005",
                Barcode = "BC-20261011-0005",
                TestOrderId = "ORD-105",
                PatientId = "USR-PAT-05",
                PatientName = "PhÃƒÂ¡Ã‚ÂºÃ‚Â¡m QuÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœc TuÃƒÂ¡Ã‚ÂºÃ‚Â¥n",
                PatientGender = "Nam",
                PatientAge = 55,
                TestPackageName = "Ãƒâ€žÃ‚ÂÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹nh lÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£ng Lipid mÃƒÆ’Ã‚Â¡u (Cholesterol, Triglyceride)",
                SampleType = "Serum",
                Status = SampleStatus.Completed,
                Priority = SamplePriority.Normal,
                LabRoom = "PhÃƒÆ’Ã‚Â²ng Lab Sinh HÃƒÆ’Ã‚Â³a 102",
                AssignedTechnicianName = "KTV. LÃƒÆ’Ã‚Âª ThÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ BÃƒÆ’Ã‚Â¬nh",
                CollectedAt = now.AddHours(-3),
                ReceivedAt = now.AddHours(-2),
                CompletedAt = now.AddMinutes(-20)
            }
        });

        // Phlebotomy collection lists are dynamically persisted to MongoDB or loaded at runtime

        // Seed default IoT thresholds (UC67)
        IotThresholds.AddRange(new[]
        {
            new IotDeviceThreshold { DeviceId = "TEMP-001", DeviceName = "Fridge Temperature Sensor 1", Location = "Fridge R-01", SensorType = "Temperature", Unit = "Ãƒâ€šÃ‚Â°C", LowAlert = 2.0, HighAlert = 8.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "TEMP-002", DeviceName = "Fridge Temperature Sensor 2", Location = "Fridge R-02", SensorType = "Temperature", Unit = "Ãƒâ€šÃ‚Â°C", LowAlert = 2.0, HighAlert = 8.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "TEMP-003", DeviceName = "Freezer Temperature Sensor", Location = "Freezer F-01", SensorType = "Temperature", Unit = "Ãƒâ€šÃ‚Â°C", LowAlert = -25.0, HighAlert = -15.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "TEMP-004", DeviceName = "Ambient Lab Temperature", Location = "Lab Room 01", SensorType = "Temperature", Unit = "Ãƒâ€šÃ‚Â°C", LowAlert = 18.0, HighAlert = 26.0, IntervalSeconds = 120, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "HUM-001", DeviceName = "Cold Storage Humidity", Location = "Cold Storage", SensorType = "Humidity", Unit = "%", LowAlert = 30.0, HighAlert = 60.0, IntervalSeconds = 60, Status = "Active" },
            new IotDeviceThreshold { DeviceId = "HUM-002", DeviceName = "Lab Room Humidity", Location = "Lab Room 01", SensorType = "Humidity", Unit = "%", LowAlert = 30.0, HighAlert = 60.0, IntervalSeconds = 120, Status = "Active" }
        });

        // Seed default IoT alert recipients (UC67)
        IotAlertRecipients.AddRange(new[]
        {
            new IotAlertRecipient { FullName = "TrÃƒÂ¡Ã‚ÂºÃ‚Â§n Minh TuÃƒÂ¡Ã‚ÂºÃ‚Â¥n", Role = "Manager", Email = "tuan.tran@bms.health", Phone = "+84 901 234 567", Channels = new() { "Push", "Email" } },
            new IotAlertRecipient { FullName = "VÃƒÆ’Ã‚Âµ Minh Khoa", Role = "Cold storage lead", Email = "khoa.vo@bms.health", Phone = "+84 902 345 678", Channels = new() { "Push", "Email", "SMS" } },
            new IotAlertRecipient { FullName = "PhÃƒÂ¡Ã‚ÂºÃ‚Â¡m Thu Dung", Role = "Lab lead", Email = "dung.pham@bms.health", Phone = "+84 903 456 789", Channels = new() { "Push", "Email" } }
        });

        // Seed initial technical test verification checklist (UC72)
        TestVerifications.Add(new TestResultVerification
        {
            Id = "VERIF-261006-0089",
            SpecimenId = "SPC-261006-A1-0089",
            OrderId = "ORD-261006-0187",
            PackageName = "Lipid Profile",
            PatientId = "PT-2026-004523",
            PatientName = "NguyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¦n VÃƒâ€žÃ†â€™n An",
            Status = "PendingVerification",
            CreatedAt = now.AddMinutes(-45),
            Checklist = new List<VerificationCheckItem>
            {
                new VerificationCheckItem { CheckName = "Patient identity confirmed", Status = "OK", StatusType = "ok", Details = "NguyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¦n VÃƒâ€žÃ†â€™n An matches order ORD-261006-0187" },
                new VerificationCheckItem { CheckName = "Analyzer QC passed", Status = "Flag", StatusType = "warn", Details = "QCF-1142 active (1-3s) on BC-01 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â under review" },
                new VerificationCheckItem { CheckName = "Reagent lot valid", Status = "OK", StatusType = "ok", Details = "LOT-7721 Ãƒâ€šÃ‚Â· Expires 31 Dec 2026" },
                new VerificationCheckItem { CheckName = "Delta check vs. baseline", Status = "Flag", StatusType = "warn", Details = "LDL +0.42 mmol/L vs. Jun 2026 ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â rerun confirmed same value" },
                new VerificationCheckItem { CheckName = "Panic / critical values", Status = "None", StatusType = "ok", Details = "No critical values detected in this specimen" },
                new VerificationCheckItem { CheckName = "Reference intervals applied", Status = "OK", StatusType = "ok", Details = "Adult male 18-65y profile Ãƒâ€šÃ‚Â· Age-specific" },
                new VerificationCheckItem { CheckName = "Technical notes complete", Status = "OK", StatusType = "ok", Details = "3 notes attached by PhÃƒÂ¡Ã‚ÂºÃ‚Â¡m Thu Dung" },
                new VerificationCheckItem { CheckName = "Rerun results", Status = "Confirmed", StatusType = "ok", Details = "LDL 3.95 on repeat ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â matches original" }
            }
        });

        // Seed initial sample origin trace and custody chain (UC77)
                MedicalAssessments.Add(new MedicalAssessment
        {
            Id = "ASM-261021-0001",
            AssessmentCode = "ASM-261021-0001",
            PatientId = "USR-PAT-01",
            PatientCode = "PAT-261019-0005",
            PatientName = "B?nh NhÃƒÂ¢n VÃƒÂµ Gia Huy",
            PatientDob = new DateTime(1995, 5, 20),
            PatientGender = "Male",
            EncounterId = "ENC-261021-001",
            EncounterType = "ClinicVisit",
            EncounterDate = DateTime.UtcNow.AddHours(-2),
            DoctorId = "USR-DOC-01",
            DoctorName = "BS. Nguy?n Van An",
            Department = "Khoa KhÃƒÂ¡m B?nh",
            AssessmentDate = DateTime.UtcNow.AddHours(-2),
            ChiefComplaint = "M?t m?i kÃƒÂ©o dÃƒÂ i, khÃƒÂ¡t nu?c nhi?u vÃƒÂ  s?t cÃƒÂ¢n khÃƒÂ´ng rÃƒÂµ nguyÃƒÂªn nhÃƒÂ¢n",
            ClinicalSymptoms = "B?nh nhÃƒÂ¢n c?m th?y m?t m?i 2 tu?n nay, ti?u dÃƒÂªm 3-4 l?n/dÃƒÂªm, u?ng nhi?u nu?c.",
            Vitals = new VitalSigns
            {
                HeartRate = 78,
                SystolicBp = 125,
                DiastolicBp = 82,
                Temperature = 36.8m,
                Spo2 = 98,
                HeightCm = 172m,
                WeightKg = 70m,
                Bmi = 23.66m,
                BmiCategory = "Th?a cÃƒÂ¢n (Overweight)"
            },
            PhysicalExamination = "Tim d?u, T1 T2 rÃƒÂµ, khÃƒÂ´ng ÃƒÂ¢m th?i b?nh lÃƒÂ½. Ph?i trong, khÃƒÂ´ng rale. B?ng m?m, gan lÃƒÂ¡ch khÃƒÂ´ng s? ch?m.",
            Icd10Code = "E11",
            Icd10Name = "ÃƒÂÃƒÂ¡i thÃƒÂ¡o du?ng typ 2 (Type 2 diabetes mellitus)",
            PreliminaryDiagnosis = "Theo dÃƒÂµi ÃƒÂÃƒÂ¡i thÃƒÂ¡o du?ng typ 2",
            FinalDiagnosis = "ÃƒÂÃƒÂ¡i thÃƒÂ¡o du?ng typ 2 chua cÃƒÂ³ bi?n ch?ng",
            Severity = "Moderate",
            RiskLevel = "Medium",
            ClinicalImpression = "Nghi ng? dÃƒÂ¡i thÃƒÂ¡o du?ng m?i kh?i phÃƒÂ¡t, c?n xÃƒÂ©t nghi?m du?ng huy?t dÃƒÂ³i vÃƒÂ  HbA1c kh?n.",
            RecommendedTestPackages = new List<string> { "GÃƒÂ³i Sinh HÃƒÂ³a MÃƒÂ¡u Co B?n", "XÃƒÂ©t nghi?m HbA1c", "ÃƒÂu?ng huy?t dÃƒÂ³i (Fasting Glucose)" },
            TreatmentPlan = "Ch? d? an gi?m tinh b?t, h?n ch? du?ng ng?t. T?p th? d?c 30 phÃƒÂºt/ngÃƒÂ y.",
            PrescriptionNotes = "Chua dÃƒÂ¹ng thu?c h? du?ng huy?t cho d?n khi cÃƒÂ³ k?t qu? xÃƒÂ©t nghi?m chÃƒÂ­nh th?c.",
            FollowUpDate = DateTime.UtcNow.AddDays(7),
            FollowUpNotes = "TÃƒÂ¡i khÃƒÂ¡m sau 1 tu?n kÃƒÂ¨m k?t qu? xÃƒÂ©t nghi?m d? kÃƒÂª toa phÃƒÂ¹ h?p.",
            Status = "Finalized",
            IsFinalized = true,
            FinalizedAt = DateTime.UtcNow.AddHours(-1),
            FinalizedBy = "BS. Nguy?n Van An",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            CreatedBy = "USR-DOC-01"
        });

        SampleOriginTraces.Add(new SampleOriginTrace
        {
            Id = "TRACE-261006-0089",
            SpecimenId = "SPC-261006-A1-0089",
            OrderId = "ORD-261006-0187",
            PatientId = "PT-004523",
            PatientName = "NguyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¦n VÃƒâ€žÃ†â€™n An",
            CreatedAt = now.AddMinutes(-40),
            Custody = new ChainOfCustody
            {
                Integrity = "Unbroken",
                BarcodeScansCount = 7,
                TransportDuration = "12 min (Room 02 ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ Chem bench)",
                AnalyzerModel = "BC-01 Ãƒâ€šÃ‚Â· Calibrated 07:30",
                ReagentLot = "LOT-7721 Ãƒâ€šÃ‚Â· Exp 31 Dec 2026",
                QcStatus = "QCF-1142",
                RerunCount = 1,
                NotesCount = 3
            },
            Nodes = new List<TraceNode>
            {
                new TraceNode { NodeType = "Patient", Title = "NguyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¦n VÃƒâ€žÃ†â€™n An", Subtitle = "PT-004523", Code = "PT-004523", Badge = "Patient", BadgeType = "patient" },
                new TraceNode { NodeType = "Doctor", Title = "Dr. TrÃƒÂ¡Ã‚ÂºÃ‚Â§n ThÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ BÃƒÆ’Ã‚Â¬nh", Subtitle = "Internal Medicine", Code = "DOC-01", Badge = "Doctor", BadgeType = "doctor" },
                new TraceNode { NodeType = "Order", Title = "ORD-261006-0187", Subtitle = "Lipid + Glucose", Code = "ORD-261006-0187", Badge = "Order", BadgeType = "order" },
                new TraceNode { NodeType = "Collection", Title = "PhÃƒÂ¡Ã‚ÂºÃ‚Â¡m Thu Dung", Subtitle = "Lab Room 02 Ãƒâ€šÃ‚Â· 08:05", Code = "COL-01", Badge = "Collection", BadgeType = "warn" },
                new TraceNode { NodeType = "Specimen", Title = "SPC-0089", Subtitle = "SST 4mL", Code = "SPC-261006-A1-0089", Badge = "Specimen", BadgeType = "err" },
                new TraceNode { NodeType = "Analyzer", Title = "BC-01", Subtitle = "LOT-7721 Ãƒâ€šÃ‚Â· BATCH-02", Code = "BC-01", Badge = "Analyzer", BadgeType = "warn" },
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
