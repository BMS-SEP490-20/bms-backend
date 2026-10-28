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

        // Iteration 3: Core Medical & Tracking Services
        services.AddScoped<ITrackTestProgressService, TrackTestProgressService>();
        services.AddScoped<IQcService, QcService>();

        // Iteration 3: Analytics & Reporting Services
        services.AddScoped<ITatStatisticsService, TatStatisticsService>();
        services.AddScoped<IRevenueReportService, RevenueReportService>();

        return services;
    }
}