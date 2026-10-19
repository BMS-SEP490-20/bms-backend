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
        services.AddScoped<IOtpRepository, OtpRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<BMS.Core.Interfaces.ITestCriterionRepository, TestCriterionRepository>();
        services.AddScoped<BMS.Application.Interfaces.ITestCriterionRepository, TestCriterionRepository>();
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<ISmsService, SmsService>();
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<ISampleRepository, SampleRepository>();

        return services;
    }
}
