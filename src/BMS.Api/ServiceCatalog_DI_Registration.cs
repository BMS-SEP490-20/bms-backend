using System.Reflection;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Infrastructure.Repositories;
using Microsoft.OpenApi;
using MongoDB.Driver;

namespace BMS.Api;

/// MongoDB, Swagger, and CORS into the DI container.
public static class ServiceCatalogDiRegistration
{
    /// Registers MongoDB client/database, TestPackage repository and service.
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration (for connection string).</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddServiceCatalog(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── MongoDB ──────────────────────────────────────────────────────────
        var connectionString = configuration.GetConnectionString("MongoDb")
            ?? "mongodb://localhost:27017";

        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));

        services.AddScoped(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            return client.GetDatabase("BMS_Database");
        });

        // ── UC01 Repository & Service ────────────────────────────────────────
        services.AddScoped<ITestPackageRepository, TestPackageRepository>();
        services.AddScoped<ITestPackageService, TestPackageService>();

        return services;
    }
    /// Registers Swagger / OpenAPI with optional XML documentation comments.=
    /// <param name="services">The service collection.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>=
    public static IServiceCollection AddBmsSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "BMS API",
                Version = "v1",
                Description = "Blood Management & Testing System API"
            });

            // Include XML comments generated from /// docs (enable GenerateDocumentationFile in .csproj)
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }

    /// Registers a CORS policy that allows the React frontend on port 3000.
    /// <param name="services">The service collection.</param>
    /// <param name="policyName">CORS policy name (default: AllowFrontend).</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddBmsCors(
        this IServiceCollection services,
        string policyName = "AllowFrontend")
    {
        services.AddCors(options =>
        {
            options.AddPolicy(policyName, policy =>
                policy.WithOrigins("http://localhost:3000")
                      .AllowAnyHeader()
                      .AllowAnyMethod());
        });

        return services;
    }

    /// Enables Swagger UI middleware (call only in Development).
    /// <param name="app">The web application.</param>
    /// <returns>The same <see cref="WebApplication"/> for chaining.</returns>
    public static WebApplication UseBmsSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "BMS API v1");
        });

        return app;
    }
}