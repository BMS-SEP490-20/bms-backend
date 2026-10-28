using Microsoft.OpenApi;
using MongoDB.Driver;
using System.Reflection;

namespace BMS.Api.DependencyInjection;

public static class InfrastructureDi
{
    public static IServiceCollection AddBmsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("MongoDb")
            ?? "mongodb://localhost:27017";

        services.AddSingleton<IMongoClient>(_ => new MongoClient(cs));
        services.AddScoped(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase("BMS_Database"));

        return services;
    }

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
            c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
            c.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
            var xml = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var path = Path.Combine(AppContext.BaseDirectory, xml);
            if (File.Exists(path)) c.IncludeXmlComments(path);
        });
        return services;
    }

    public static IServiceCollection AddBmsCors(
        this IServiceCollection services,
        string policyName = "AllowFrontend")
    {
        services.AddCors(o => o.AddPolicy(policyName, p =>
            p.WithOrigins("http://localhost:3000")
             .AllowAnyHeader()
             .AllowAnyMethod()));
        return services;
    }

    public static WebApplication UseBmsSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "BMS API v1"));
        return app;
    }
}