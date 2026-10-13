using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Infrastructure.Repositories;

namespace BMS.Api;

/// <summary>
/// DI registration for UC16 – View Doctor Schedule.
/// </summary>
public static class DoctorScheduleDi
{
    public static IServiceCollection AddDoctorSchedule(this IServiceCollection services)
    {
        services.AddScoped<IDoctorScheduleRepository, DoctorScheduleRepository>();
        services.AddScoped<IDoctorScheduleService, DoctorScheduleService>();
        return services;
    }
}