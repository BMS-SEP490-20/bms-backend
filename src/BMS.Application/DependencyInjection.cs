using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Schedule.Services;
using BMS.WalkIn.Services;
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

        return services;
    }
}
