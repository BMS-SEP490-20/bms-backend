using Microsoft.EntityFrameworkCore;
using TestOrder.Models.DTO;
using TestOrder.Repositories.Interface;
using TestOrder.Services.Interface;
using Testorder_service.Models.Entities;
using Testorder_service.Services.Interface;
using Testorder_service.GrpcClients;
using Testorder_service.Data;

namespace TestOrder.Services.Implement
{
    public class AppointmentService : IAppointmentService
    {
        private readonly IAppointmentRepository _repo;
        private readonly IBloodSampleRepository _bloodSampleRepo;
        private readonly IEmailNotificationService _emailNotificationService;
        private readonly IamGrpcClient _iamGrpcClient;
        private readonly TestOrderManagementDbContext _context;
        private readonly ILogger<AppointmentService> _logger;

        public AppointmentService(
            IAppointmentRepository repo, 
            IBloodSampleRepository bloodSampleRepo,
            IEmailNotificationService emailNotificationService,
            IamGrpcClient iamGrpcClient,
            TestOrderManagementDbContext context,
            ILogger<AppointmentService> logger)
        {
            _repo = repo;
            _bloodSampleRepo = bloodSampleRepo;
            _emailNotificationService = emailNotificationService;
            _iamGrpcClient = iamGrpcClient;
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<AppointmentReadDto>> GetAllAsync()
        {
            var list = await _repo.GetAllAsync();
            return list.Select(a => new AppointmentReadDto
            {
                AppointmentId = a.AppointmentId,
                TestPackageId = a.TestPackageId,
                DoctorShiftId = a.DoctorShiftId,
                Status = a.Status,
                CreatedBy = a.CreatedBy,
                CreatedAt = a.CreatedAt,
                LabAssignmentId = a.LabAssignmentId,
                AppointmentTime = a.AppointmentTime,
                AppointmentType = a.AppointmentType,
                ReasonForRescheduling = a.ReasonForRescheduling,
                GuestName = a.GuestName,
                GuestPhone = a.GuestPhone
            });
        }

        public async Task<AppointmentReadDto?> GetByIdAsync(string id)
        {
            var a = await _repo.GetByIdAsync(id);
            if (a == null) return null;

            // Log để debug TestPackage
            Console.WriteLine($"[AppointmentService] GetByIdAsync - AppointmentId: {a.AppointmentId}, TestPackageId: {a.TestPackageId}, TestPackage: {(a.TestPackage != null ? a.TestPackage.PackageName : "null")}");

            return new AppointmentReadDto
            {
                AppointmentId = a.AppointmentId,
                TestPackageId = a.TestPackageId,
                TestPackageName = a.TestPackage?.PackageName,
                DoctorShiftId = a.DoctorShiftId,
                Status = a.Status,
                CreatedBy = a.CreatedBy,
                CreatedAt = a.CreatedAt,
                LabAssignmentId = a.LabAssignmentId,
                AppointmentTime = a.AppointmentTime,
                AppointmentType = a.AppointmentType,
                ReasonForRescheduling = a.ReasonForRescheduling,
                GuestName = a.GuestName,
                GuestPhone = a.GuestPhone
            };
        }

       public async Task<AppointmentReadDto> CreateAsync(AppointmentCreateDto dto)
{
    var random = new Random();
    string? labAssignmentId = dto.LabAssignmentId;

    // Nếu không cung cấp LabAssignmentId → tự auto-assign
    if (string.IsNullOrWhiteSpace(labAssignmentId) && !string.IsNullOrWhiteSpace(dto.DoctorShiftId))
    {
        try
        {
            // Lấy ca trực
            var doctorShift = await _context.DoctorShifts
                .AsNoTracking()
                .FirstOrDefaultAsync(ds => ds.ShiftId == dto.DoctorShiftId);

            if (doctorShift != null)
            {
                // 🔥 NEW: Chọn lab assignment được dùng ít nhất
                var leastUsedLab = await GetLeastUsedLabAssignmentAsync(doctorShift);

                if (leastUsedLab != null)
                {
                    labAssignmentId = leastUsedLab.AssignmentId;

                    _logger.LogInformation(
                        "✅ Assigned least-used LabAssignment {LabId} for Appointment (DoctorShiftId: {ShiftId})",
                        labAssignmentId, dto.DoctorShiftId);
                }
                else
                {
                    _logger.LogWarning(
                        "⚠ No lab assignment found for DoctorShiftId {ShiftId} (Date: {Date}, Time: {Start}-{End})",
                        dto.DoctorShiftId, doctorShift.ShiftDate, doctorShift.ShiftStart, doctorShift.ShiftEnd);
                }
            }
            else
            {
                _logger.LogWarning("⚠ DoctorShift not found: {DoctorShiftId}", dto.DoctorShiftId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error selecting least-used lab assignment for DoctorShiftId: {DoctorShiftId}", dto.DoctorShiftId);
        }
    }

    // Tạo entity
    var entity = new Appointment
    {
        AppointmentId = $"APP{random.Next(0, 999999):D6}",
        TestPackageId = dto.TestPackageId,
        DoctorShiftId = dto.DoctorShiftId,
        Status = dto.Status,
        CreatedBy = dto.CreatedBy,
        CreatedAt = DateTime.Now,
        LabAssignmentId = labAssignmentId,
        AppointmentTime = dto.AppointmentTime,
        AppointmentType = dto.AppointmentType,
        GuestName = dto.GuestName,
        GuestPhone = dto.GuestPhone
    };

    // Lưu
    var created = await _repo.CreateAsync(entity);

    _logger.LogInformation(
        "✅ Created Appointment {Id} with LabAssignmentId: {LabId}",
        created.AppointmentId, created.LabAssignmentId ?? "null");

    return new AppointmentReadDto
    {
        AppointmentId = created.AppointmentId,
        TestPackageId = created.TestPackageId,
        DoctorShiftId = created.DoctorShiftId,
        Status = created.Status,
        CreatedBy = created.CreatedBy,
        CreatedAt = created.CreatedAt,
        LabAssignmentId = created.LabAssignmentId,
        AppointmentTime = created.AppointmentTime,
        AppointmentType = created.AppointmentType,
        GuestName = created.GuestName,
        GuestPhone = created.GuestPhone
    };
    }
        private async Task<int> CountUsedAsync(string labAssignmentId)
        {
            return await _context.Appointments
                .AsNoTracking()
                .CountAsync(a => a.LabAssignmentId == labAssignmentId);
        }

        private async Task<LabAssignment?> GetLeastUsedLabAssignmentAsync(DoctorShift doctorShift)
        {
            var validLabs = await _context.LabAssignments
                .AsNoTracking()
                .Where(la => la.AssignmentDate == doctorShift.ShiftDate &&
                             la.ShiftStart == doctorShift.ShiftStart &&
                             la.ShiftEnd == doctorShift.ShiftEnd &&
                             (la.Status == "Scheduled" || la.Status == "Active"))
                .ToListAsync();

            if (!validLabs.Any())
                return null;

            LabAssignment? leastUsed = null;
            int minCount = int.MaxValue;

            foreach (var lab in validLabs)
            {
                int usedCount = await CountUsedAsync(lab.AssignmentId);

                if (usedCount < minCount)
                {
                    minCount = usedCount;
                    leastUsed = lab;
                }
            }

            return leastUsed;
        }


        public async Task<bool> UpdateAsync(string id, AppointmentUpdateDto dto)
        {
            var entity = await _repo.GetByIdAsync(id);
            if (entity == null) return false;

            var oldStatus = entity.Status;

            entity.TestPackageId = dto.TestPackageId ?? entity.TestPackageId;
            entity.DoctorShiftId = dto.DoctorShiftId ?? entity.DoctorShiftId;
            entity.Status = dto.Status ?? entity.Status;
            entity.LabAssignmentId = dto.LabAssignmentId ?? entity.LabAssignmentId;
            entity.AppointmentTime = dto.AppointmentTime ?? entity.AppointmentTime;
            entity.AppointmentType = dto.AppointmentType ?? entity.AppointmentType;
            entity.ReasonForRescheduling = dto.ReasonForRescheduling ?? entity.ReasonForRescheduling;
            entity.GuestName = dto.GuestName ?? entity.GuestName;
            entity.GuestPhone = dto.GuestPhone ?? entity.GuestPhone;
            await _repo.UpdateAsync(entity);

            if (oldStatus != "checked_in" && entity.Status == "checked_in")
            {
                var random = new Random();
                var bloodSample = new BloodSample
                {
                    SampleId = $"SA{random.Next(0, 999999):D6}",
                    AppointmentId = entity.AppointmentId,
                    SampleType = "Blood",
                    CollectedAt = DateTime.Now,
                    Status = "pending_test",
                    StorageLocation = "Lab Room 1",
                    Notes = "Auto-generated after patient checked in"
                };

                await _bloodSampleRepo.CreateAsync(bloodSample);
            }

            // Send email notification when status changes to "scheduled" (đặt lịch)
            if (oldStatus != "scheduled" && entity.Status == "scheduled")
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendAppointmentScheduledEmailAsync(entity);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to send email notification for Appointment {AppointmentId}", entity.AppointmentId);
                    }
                });
            }

            return true;
        }

        private async Task SendAppointmentScheduledEmailAsync(Appointment appointment)
        {
            try
            {
                // Get appointment with all related data
                var appointmentWithDetails = await _context.Appointments
                    .Include(a => a.TestPackage)
                    .Include(a => a.DoctorShift)
                        .ThenInclude(ds => ds.Doctor)
                    .Include(a => a.DoctorShift)
                        .ThenInclude(ds => ds.Room)
                    .FirstOrDefaultAsync(a => a.AppointmentId == appointment.AppointmentId);

                if (appointmentWithDetails == null)
                {
                    _logger.LogWarning("⚠️ Appointment with details not found for {AppointmentId}", appointment.AppointmentId);
                    return;
                }

                // Get patient email from iam-service using CreatedBy (userId)
                var userResponse = await _iamGrpcClient.GetUserByIdAsync(appointmentWithDetails.CreatedBy);
                if (userResponse == null || string.IsNullOrWhiteSpace(userResponse.Email))
                {
                    _logger.LogWarning("⚠️ Patient email not found for userId {UserId}", appointmentWithDetails.CreatedBy);
                    return;
                }

                // Extract appointment details
                var testPackageName = appointmentWithDetails.TestPackage?.PackageName ?? "Không xác định";
                var doctorName = appointmentWithDetails.DoctorShift?.Doctor?.FullName ?? "Chưa phân công";
                var roomName = appointmentWithDetails.DoctorShift?.Room?.RoomName ?? "Chưa phân công";

                // Calculate appointment date/time from DoctorShift
                DateTime appointmentDateTime = DateTime.Now; // Default
                if (appointmentWithDetails.DoctorShift != null)
                {
                    var shiftDate = appointmentWithDetails.DoctorShift.ShiftDate;
                    var shiftStart = appointmentWithDetails.DoctorShift.ShiftStart;
                    appointmentDateTime = shiftDate.ToDateTime(shiftStart);
                }

                await _emailNotificationService.SendAppointmentScheduledEmailAsync(
                    userResponse.Email,
                    appointmentWithDetails.AppointmentId,
                    testPackageName,
                    appointmentDateTime,
                    doctorName,
                    roomName
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending Appointment scheduled email for {AppointmentId}", appointment.AppointmentId);
            }
        }

        public async Task<bool> DeleteAsync(string id)
        {
            var entity = await _repo.GetByIdAsync(id);
            if (entity == null) return false;

            await _repo.DeleteAsync(entity);
            return true;
        }

        public async Task<IEnumerable<AppointmentReadDto>> GetByPatientIdAsync(string patientId)
        {
            var list = await _repo.GetByPatientIdAsync(patientId);
            return list.Select(a => new AppointmentReadDto
            {
                AppointmentId = a.AppointmentId,
                TestPackageId = a.TestPackageId,
                DoctorShiftId = a.DoctorShiftId,
                Status = a.Status,
                CreatedBy = a.CreatedBy,
                CreatedAt = a.CreatedAt,
                LabAssignmentId = a.LabAssignmentId,
                AppointmentTime = a.AppointmentTime,
                AppointmentType = a.AppointmentType,
                ReasonForRescheduling = a.ReasonForRescheduling,
                GuestName = a.GuestName,
                GuestPhone = a.GuestPhone
            });
        }
    }
}
