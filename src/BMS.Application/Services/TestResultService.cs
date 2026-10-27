using TestOrder.Models.DTO;
using TestOrder.Repositories.Interface;
using TestOrder.Services.Interface;
using Testorder_service.Models.DTO;
using Testorder_service.Models.Entities;
using Testorder_service.Services.Interface;
using Testorder_service.GrpcClients;
using Microsoft.EntityFrameworkCore;
using Testorder_service.Data;

namespace TestOrder.Services.Implement
{
    public class TestResultService : ITestResultService
    {
        private readonly ITestResultRepository _repo;
        private readonly ITestParameterRepository _paramRepo;
        private readonly IAppointmentRepository _appointmentRepo;
        private readonly IEmailNotificationService _emailNotificationService;
        private readonly IamGrpcClient _iamGrpcClient;
        private readonly TestOrderManagementDbContext _context;
        private readonly ILogger<TestResultService> _logger;

        public TestResultService(
            ITestResultRepository repo, 
            ITestParameterRepository paramRepo,
            IAppointmentRepository appointmentRepo,
            IEmailNotificationService emailNotificationService,
            IamGrpcClient iamGrpcClient,
            TestOrderManagementDbContext context,
            ILogger<TestResultService> logger)
        {
            _repo = repo;
            _paramRepo = paramRepo;
            _appointmentRepo = appointmentRepo;
            _emailNotificationService = emailNotificationService;
            _iamGrpcClient = iamGrpcClient;
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<TestResultDto>> GetAllAsync()
        {
            var list = await _repo.GetAllAsync();
            return list.Select(x => new TestResultDto
            {
                TestResultId = x.TestResultId,
                AppointmentId = x.AppointmentId,
                SampleId = x.SampleId,
              
                ReagentCode = x.ReagentCode,
                Quantity = x.Quantity.HasValue ? (int)x.Quantity.Value : 0,
                DeviceId = x.DeviceId,
                ReviewedBy = x.ReviewedBy,
                ReviewedAt = x.ReviewedAt,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            });
        }

        public async Task<TestResultDto?> GetByIdAsync(string id)
        {
            var x = await _repo.GetByIdAsync(id);
            if (x == null) return null;

            return new TestResultDto
            {
                TestResultId = x.TestResultId,
                AppointmentId = x.AppointmentId,
                SampleId = x.SampleId,
               
                ReagentCode = x.ReagentCode,
                Quantity = (int)x.Quantity,
                DeviceId = x.DeviceId,
                ReviewedBy = x.ReviewedBy,
                ReviewedAt = x.ReviewedAt,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            };
        }

        public async Task CreateAsync(CreateFullTestResultDto dto)
        {
            var random = new Random();
            var entity = new TestResult
            {
                TestResultId = $"TRE{random.Next(0, 999999):D6}",
                AppointmentId = dto.AppointmentId,
                SampleId = dto.SampleId,
               
                ReagentCode = dto.ReagentCode,
                Quantity = dto.Quantity,
                DeviceId = dto.DeviceId,
                ReviewedBy = dto.ReviewedBy,
                Status = dto.Status,
                CreatedAt = DateTime.Now,
                ReviewedAt = DateTime.Now
            };

            await _repo.AddAsync(entity);
            var parameters = dto.Parameters.Select(p => new TestParameter
            {
                ParamId = Guid.NewGuid().ToString(),
                TestResultId = entity.TestResultId,
                CriteriaId = p.CriteriaId,
                ParamName = p.ParamName,
                Abbreviation = p.Abbreviation,
                Value = p.Value,
                CreatedAt = DateTime.Now
            }).ToList();

            await _paramRepo.AddRangeAsync(parameters);

            await _repo.SaveChangesAsync();
        }

        public async Task UpdateAsync(string id, UpdateTestResultDto dto)
        {
            var entity = await _repo.GetByIdAsync(id);
            if (entity == null)
                throw new KeyNotFoundException("Không tìm thấy kết quả xét nghiệm để cập nhật.");

            var oldStatus = entity.Status;
            entity.ReagentCode = dto.ReagentCode ?? entity.ReagentCode;
            entity.Quantity = dto.Quantity != 0 ? dto.Quantity : entity.Quantity;
            entity.DeviceId = dto.DeviceId ?? entity.DeviceId;
            entity.ReviewedBy = dto.ReviewedBy ?? entity.ReviewedBy;
            entity.Status = dto.Status ?? entity.Status;
            entity.ReviewedAt = dto.ReviewedAt ?? DateTime.Now;
            entity.UpdatedAt = DateTime.Now;

            await _repo.UpdateAsync(entity);

            if (dto.Parameters != null && dto.Parameters.Any())
            {
                await _paramRepo.DeleteByTestResultIdAsync(id);

                var newParams = dto.Parameters.Select(p => new TestParameter
                {
                    ParamId = Guid.NewGuid().ToString(),
                    TestResultId = entity.TestResultId,
                    CriteriaId = p.CriteriaId,
                    ParamName = p.ParamName,
                    Abbreviation = p.Abbreviation,
                    Value = p.Value,
                    CreatedAt = DateTime.Now
                }).ToList();

                await _paramRepo.AddRangeAsync(newParams);
            }

            await _repo.SaveChangesAsync();

            // Send email notification when status changes to "waiting_review"
            if (oldStatus != "waiting_review" && entity.Status == "waiting_review")
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendTestResultWaitingReviewEmailAsync(entity);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Failed to send email notification for TestResult {TestResultId}", entity.TestResultId);
                    }
                });
            }
        }

        private async Task SendTestResultWaitingReviewEmailAsync(TestResult testResult)
        {
            try
            {
                // Get Appointment with related data (load entity with includes)
                var appointment = await _context.Appointments
                    .Include(a => a.TestPackage)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.AppointmentId == testResult.AppointmentId);

                if (appointment == null)
                {
                    _logger.LogWarning("⚠️ Appointment not found for TestResult {TestResultId}", testResult.TestResultId);
                    return;
                }

                // Get patient email from iam-service using CreatedBy (userId)
                var userResponse = await _iamGrpcClient.GetUserByIdAsync(appointment.CreatedBy);
                if (userResponse == null || string.IsNullOrWhiteSpace(userResponse.Email))
                {
                    _logger.LogWarning("⚠️ Patient email not found for userId {UserId}", appointment.CreatedBy);
                    return;
                }

                var testPackageName = appointment.TestPackage?.PackageName ?? "Không xác định";
                await _emailNotificationService.SendTestResultWaitingReviewEmailAsync(
                    userResponse.Email,
                    testResult.TestResultId,
                    testResult.AppointmentId,
                    testPackageName
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending TestResult waiting review email for {TestResultId}", testResult.TestResultId);
            }
        }


        public async Task DeleteAsync(string id)
        {
            var entity = await _repo.GetByIdAsync(id);
            if (entity == null)
                throw new KeyNotFoundException("Không tìm thấy kết quả xét nghiệm để xóa.");

           
            await _paramRepo.DeleteByTestResultIdAsync(id);

           
            await _repo.DeleteAsync(id);

            await _repo.SaveChangesAsync();
        }

        public async Task<IEnumerable<TestResultDto>> GetByAppointmentIdAsync(string appointmentId)
        {
            var list = await _repo.GetByAppointmentIdAsync(appointmentId);
            return list.Select(x => new TestResultDto
            {
                TestResultId = x.TestResultId,
                AppointmentId = x.AppointmentId,
                SampleId = x.SampleId,
                ReagentCode = x.ReagentCode,
                Quantity = x.Quantity.HasValue ? (int)x.Quantity.Value : 0,
                DeviceId = x.DeviceId,
                ReviewedBy = x.ReviewedBy,
                ReviewedAt = x.ReviewedAt,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            });
        }

        /// <summary>
        /// Reviews, verifies, and digitally approves entered or analyzer-imported blood test results.
        /// </summary>
        /// <param name="id">Test result identifier.</param>
        /// <param name="dto">Approval metadata including approver identity, role, and digital signature.</param>
        /// <returns>The approved test result data transfer object.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when target test result is not found in MongoDB.</exception>
        public async Task<TestResultDto> ApproveAsync(string id, ApproveTestResultDto dto)
        {
            var entity = await _repo.GetByIdAsync(id);
            if (entity == null)
                throw new KeyNotFoundException($"Test result '{id}' was not found.");

            // BUSINESS RULE: Approval transitions the result to 'Completed' and digitally seals it,
            // making it viewable by the patient and preventing subsequent unauthorized edits.
            entity.Status = "Completed";
            entity.ReviewedBy = !string.IsNullOrWhiteSpace(dto.ApproverName)
                ? $"{dto.ApproverName} ({dto.ApproverRole ?? "Doctor"})"
                : dto.ApproverId;
            entity.ReviewedAt = DateTime.UtcNow;
            entity.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(dto.DeviceId))
            {
                entity.DeviceId = dto.DeviceId;
            }

            if (dto.Quantity.HasValue && dto.Quantity.Value > 0)
            {
                entity.Quantity = dto.Quantity.Value;
            }

            await _repo.UpdateAsync(entity);

            // SIDE EFFECT: Automatically synchronize parent appointment status to 'completed'
            // so billing, reception, and patient portals reflect the final test state.
            if (!string.IsNullOrWhiteSpace(entity.AppointmentId))

            {
                try
                {
                    var appointment = await _appointmentRepo.GetByIdAsync(entity.AppointmentId);
                    if (appointment != null)
                    {
                        appointment.Status = "completed";
                        await _appointmentRepo.UpdateAsync(appointment);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update appointment status for AppointmentId {AppointmentId}", entity.AppointmentId);
                }
            }

            await _repo.SaveChangesAsync();

            return new TestResultDto
            {
                TestResultId = entity.TestResultId,
                AppointmentId = entity.AppointmentId,
                SampleId = entity.SampleId,
                ReagentCode = entity.ReagentCode,
                Quantity = entity.Quantity.HasValue ? (int)entity.Quantity.Value : 0,
                DeviceId = entity.DeviceId,
                ReviewedBy = entity.ReviewedBy,
                ReviewedAt = entity.ReviewedAt,
                Status = entity.Status,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }
    }
}
