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
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<IWalkInService, WalkInService>();
        services.AddScoped<ISampleCheckInService, SampleCheckInService>();
        services.AddScoped<IResultService, ResultService>();
        services.AddScoped<IMedicalRecordService, MedicalRecordService>();
        services.AddScoped<IPatientReceptionService, PatientReceptionService>();

        return services;
    }
}
