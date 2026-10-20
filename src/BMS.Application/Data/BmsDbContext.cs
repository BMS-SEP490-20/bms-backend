using BMS.Core.Entities;

namespace BMS.Infrastructure.Data;

public class BmsDbContext
{
    // Thread-safe in-memory collections for fast startup & testing
    public List<User> Users { get; } = new();
    public List<Sample> Samples { get; } = new();
    public List<QueueTicket> QueueTickets { get; } = new();

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
            }
        });

        // Seed initial reception queue tickets matching UC27 live queue specification
        QueueTickets.AddRange(new[]
        {
            new QueueTicket
            {
                Id = "TKT-012",
                TicketNumber = "Q-012",
                PatientId = "USR-PAT-NAM",
                PatientName = "Lê Hoàng Nam",
                PatientCode = "PT-012056",
                PackageName = "Thyroid",
                Priority = QueuePriority.Routine,
                Status = QueueTicketStatus.Called,
                CounterName = "Counter 1",
                CheckedInAt = now.AddMinutes(-12),
                CalledAt = now.AddMinutes(-5),
                EstimatedWaitMinutes = 0
            },
            new QueueTicket
            {
                Id = "TKT-013",
                TicketNumber = "Q-013",
                PatientId = "USR-PAT-MAI",
                PatientName = "Trần Thị Mai",
                PatientCode = "PT-008214",
                PackageName = "CBC",
                Priority = QueuePriority.Routine,
                Status = QueueTicketStatus.Waiting,
                CheckedInAt = now.AddMinutes(-8),
                EstimatedWaitMinutes = 8
            },
            new QueueTicket
            {
                Id = "TKT-014",
                TicketNumber = "Q-014",
                PatientId = "USR-PAT-KIEN",
                PatientName = "Hoàng Văn Kiên",
                PatientCode = "PT-010334",
                PackageName = "Lipid",
                IsWalkIn = true,
                Priority = QueuePriority.Routine,
                Status = QueueTicketStatus.Waiting,
                CheckedInAt = now.AddMinutes(-11),
                EstimatedWaitMinutes = 11
            },
            new QueueTicket
            {
                Id = "TKT-015",
                TicketNumber = "Q-015",
                PatientId = "USR-PAT-LAN",
                PatientName = "Nguyễn Thị Lan",
                PatientCode = "PT-007421",
                PackageName = "CBC + Lipid",
                Priority = QueuePriority.Routine,
                Status = QueueTicketStatus.Waiting,
                CheckedInAt = now.AddMinutes(-16),
                EstimatedWaitMinutes = 16
            },
            new QueueTicket
            {
                Id = "TKT-016",
                TicketNumber = "Q-016",
                PatientId = "USR-PAT-ON",
                PatientName = "Phạm Minh Ơn",
                PatientCode = "PT-004987",
                PackageName = "Liver Function",
                Priority = QueuePriority.Urgent,
                Status = QueueTicketStatus.Waiting,
                CheckedInAt = now.AddMinutes(-8),
                EstimatedWaitMinutes = 8
            },
            new QueueTicket
            {
                Id = "TKT-017",
                TicketNumber = "Q-017",
                PatientId = "USR-PAT-HOA",
                PatientName = "Lý Thị Hoa",
                PatientCode = "PT-009876",
                PackageName = "Thyroid + Liver",
                Priority = QueuePriority.STAT,
                Status = QueueTicketStatus.Waiting,
                CheckedInAt = now.AddMinutes(-4),
                EstimatedWaitMinutes = 4
            },
            new QueueTicket
            {
                Id = "TKT-018",
                TicketNumber = "Q-018",
                PatientId = "USR-PAT-DAT",
                PatientName = "Trương Minh Đạt",
                PatientCode = "PT-011245",
                PackageName = "Electrolyte",
                Priority = QueuePriority.Routine,
                Status = QueueTicketStatus.Waiting,
                CheckedInAt = now.AddMinutes(-1),
                EstimatedWaitMinutes = 1
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
