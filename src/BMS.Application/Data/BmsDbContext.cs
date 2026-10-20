using BMS.Core.Entities;

namespace BMS.Infrastructure.Data;

public class BmsDbContext
{
    // Thread-safe in-memory collections for fast startup & testing
    public List<User> Users { get; } = new();
    public List<Sample> Samples { get; } = new();
    public List<TestOrder> TestOrders { get; } = new();

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

        // Seed initial laboratory test orders (UC32)
        TestOrders.AddRange(new[]
        {
            new TestOrder
            {
                Id = "ORD-20261020-001",
                OrderNumber = "ORD-20261020-001",
                PatientId = "USR-PAT-01",
                PatientName = "Nguyễn Văn An",
                PatientGender = "Nam",
                PatientAge = 34,
                DoctorId = "USR-DOC-01",
                DoctorName = "Dr. Trần Thị Bình",
                Priority = SamplePriority.Normal,
                Status = TestOrderStatus.Ordered,
                ClinicalIndication = "Follow-up for elevated LDL cholesterol (3.95 mmol/L, Sep 2026). Also assessing fasting glucose, HbA1c, and liver enzymes.",
                SpecialInstructions = "Repeat LDL from same analyzer if possible.",
                ScheduledDate = DateTime.UtcNow.Date,
                TotalAmount = 740000m,
                FastingRequired = true,
                FastingHours = 12,
                CreatedAt = DateTime.UtcNow.AddHours(-2),
                CreatedBy = "USR-DOC-01",
                Items = new List<TestOrderItem>
                {
                    new TestOrderItem
                    {
                        TestCode = "SP-LIP-02",
                        TestName = "Lipid Profile Extended",
                        Category = "Biochemistry",
                        TubeType = "SST 4mL",
                        VolumeMl = 4.0,
                        TurnaroundTime = "~4h",
                        PreparationNotes = "Fast 12h",
                        Price = 380000m
                    },
                    new TestOrderItem
                    {
                        TestCode = "TST-GLU-F",
                        TestName = "Fasting Glucose",
                        Category = "Biochemistry",
                        TubeType = "NaF 2mL",
                        VolumeMl = 2.0,
                        TurnaroundTime = "~1h",
                        PreparationNotes = "Fast 8h",
                        Price = 80000m
                    },
                    new TestOrderItem
                    {
                        TestCode = "SP-LFT-01",
                        TestName = "Liver Function Test",
                        Category = "Biochemistry",
                        TubeType = "SST 3mL",
                        VolumeMl = 3.0,
                        TurnaroundTime = "~3h",
                        PreparationNotes = "Fast 8h",
                        Price = 280000m
                    }
                }
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
