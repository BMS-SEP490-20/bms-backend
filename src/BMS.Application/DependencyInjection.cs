using BMS.Application.Interfaces;
using BMS.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ITestCriterionService, TestCriterionService>();
        services.AddScoped<IReferenceIntervalService, ReferenceIntervalService>();
        services.AddScoped<ISpecimenHistoryService, SpecimenHistoryService>();
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IWalkInService, WalkInService>();
        services.AddScoped<ISampleCheckInService, SampleCheckInService>();
        services.AddScoped<IResultService, ResultService>();
        services.AddScoped<ITestBatchService, TestBatchService>();
        services.AddScoped<IReceiptService, ReceiptService>();
        services.AddScoped<IMedicalRecordService, MedicalRecordService>();
        services.AddScoped<IMedicalNoteService, MedicalNoteService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<ILabProfileService, LabProfileService>();
        services.AddScoped<ISampleTimelineService, SampleTimelineService>();
        services.AddScoped<ITestHistoryService, TestHistoryService>();
        services.AddScoped<IPatientReceptionService, PatientReceptionService>();

        services.AddScoped<ITrackTestProgressService, TrackTestProgressService>();

        services.AddScoped<BillingSettingsService>();
        services.AddScoped<IBillingSettingsService>(sp => sp.GetRequiredService<BillingSettingsService>());
        services.AddScoped<IFeePolicyProvider>(sp => sp.GetRequiredService<BillingSettingsService>());
        services.AddScoped<ICalculateFeeService>(sp => new CalculateFeeService(
            sp.GetRequiredService<IServicePackageRepository>(),
            sp.GetRequiredService<BMS.Core.Interfaces.ITestOrderRepository>(),
            sp.GetRequiredService<IFeePolicyProvider>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IOrderPaymentService, OrderPaymentService>();
        services.AddScoped<IOrderRefundService, OrderRefundService>();
        services.AddScoped<IPushNotificationService, PushNotificationService>();
        services.AddScoped<IQcService, QcService>();

        services.AddScoped<IIotAlertService, IotAlertService>();

        services.AddScoped<ITatStatisticsService, TatStatisticsService>();
        services.AddScoped<IRevenueReportService, RevenueReportService>();

        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<ILabRoomService, LabRoomService>();
        services.AddScoped<IBloodSampleService, BloodSampleService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<ITestResultService, TestResultService>();
        services.AddScoped<IQcFlagService, QcFlagService>();
        services.AddScoped<IPatientFeedbackService, PatientFeedbackService>();
        services.AddScoped<IPaymentRecordService, PaymentRecordService>();

        return services;
    }
}