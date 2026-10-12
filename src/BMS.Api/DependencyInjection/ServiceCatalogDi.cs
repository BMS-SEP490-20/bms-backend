using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Infrastructure.Repositories;

namespace BMS.Api;

/// <summary>
/// DI registration for UC01 – View Service Catalog.
/// </summary>
public static class ServiceCatalogDi
{
    public static IServiceCollection AddServiceCatalog(this IServiceCollection services)
    {
        services.AddScoped<ITestPackageRepository, TestPackageRepository>();
        services.AddScoped<ITestPackageService, TestPackageService>();
        return services;
    }
}