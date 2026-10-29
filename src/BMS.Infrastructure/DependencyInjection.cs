using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Interfaces;
using BMS.Infrastructure.Data;
using BMS.Infrastructure.Repositories;
using BMS.Infrastructure.Services;
using BMS.Schedule.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace BMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddSingleton<MongoDbContext>();
        // Đợt 2: Patient "own data" checks (TIEU-CHI R14).
        services.AddScoped<IPatientOwnership, PatientOwnershipRepository>();
        services.AddScoped<IOtpRepository, OtpRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<BMS.Core.Interfaces.ITestCriterionRepository, TestCriterionRepository>();
        services.AddScoped<BMS.Application.Interfaces.ITestCriterionRepository, TestCriterionRepository>();
        services.AddScoped<IReferenceIntervalRepository, ReferenceIntervalRepository>();
        services.AddScoped<ISpecimenHistoryRepository, SpecimenHistoryRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<ISmsService, SmsService>();
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<ISampleRepository, SampleRepository>();
        services.AddScoped<IResultRepository, ResultRepository>();
        services.AddScoped<ITestBatchRepository, TestBatchRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IMedicalRecordRepository, MedicalRecordRepository>();
        services.AddScoped<IMedicalNoteRepository, MedicalNoteRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<ILabProfileRepository, LabProfileRepository>();
        services.AddScoped<ISampleTimelineRepository, SampleTimelineRepository>();
        services.AddScoped<ITestHistoryRepository, TestHistoryRepository>();
        services.AddScoped<IMedicalAssessmentRepository, MedicalAssessmentRepository>();
        services.AddScoped<IBackupRepository, BackupRepository>();

        // Iteration 3 features ported from the old microservices (Đợt 1 recovery): Mongo repositories
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<ILabRoomRepository, LabRoomRepository>();
        services.AddScoped<IBloodSampleRepository, BloodSampleRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<ITestResultRepository, TestResultRepository>();
        services.AddScoped<IQcFlagRepository, QcFlagRepository>();
        services.AddScoped<IPatientFeedbackRepository, PatientFeedbackRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();

        return services;
    }
}
