using BMS.Api;
using BMS.Api.Extensions;
using BMS.Application;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Api.DependencyInjection;
using BMS.Core.Interfaces;
using BMS.Infrastructure;
using BMS.Infrastructure.Data;
using BMS.Infrastructure.Repositories;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// =========================================================
// SERVICES
// =========================================================

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

// Shared (once)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();
builder.Services.AddBmsSwagger();

// Database and authentication
builder.Services.AddMongo(builder.Configuration);
builder.Services.AddAuthModule(builder.Configuration);

// Password recovery and API security
builder.Services.AddPasswordRecovery(
    builder.Configuration,
    builder.Environment);

builder.Services.AddServicePackages();
builder.Services.AddTestCriteria();
builder.Services.AddApiHardening(builder.Configuration);

// Clean Architecture
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices();

// Development context
builder.Services.AddSingleton<BmsDbContext>();

// Main branch services
builder.Services.AddScoped<ILabQueueService, LabQueueService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IAppointmentNotificationRepository, AppointmentNotificationRepository>();
builder.Services.AddScoped<IAppointmentNotificationService, AppointmentNotificationService>();
builder.Services.AddScoped<IQueueRepository, QueueRepository>();
builder.Services.AddScoped<IQueueService, QueueService>();
builder.Services.AddScoped<BMS.Core.Interfaces.ITestOrderRepository, BMS.Infrastructure.Repositories.TestOrderRepository>();
builder.Services.AddScoped<ITestOrderService, TestOrderService>();

// IMPORTANT:
// Không đăng ký IAuthService lần nữa ở đây nếu
// AddAuthModule(), AddApplicationServices() hoặc
// AddInfrastructureServices() đã đăng ký dịch vụ này.


// =========================================================
// CORS
// =========================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("BmsFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5175",
                "http://127.0.0.1:5175",
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "http://localhost:3000"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


// =========================================================
// FORWARDED HEADERS
// =========================================================

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
});

// =========================================================
// BUILD APPLICATION
// =========================================================

// Features
builder.Services.AddServiceCatalog();
builder.Services.AddDoctorSchedule();

var app = builder.Build();

// Forwarded headers should run early in the pipeline.
app.UseForwardedHeaders();

// Exception handling (RFC 7807 ProblemDetails via GlobalExceptionHandler in all environments)
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseBmsSwagger();
}
else
{
    app.UseHsts();
}

// HTTP pipeline
app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("BmsFrontend");

app.UseAuthentication();

app.UseRateLimiter();

app.UseAuthorization();

// Controllers
app.MapControllers();

// =========================================================
// HEALTH CHECK
// =========================================================

app.MapGet("/", () => Results.Ok(new
{
    system = "BMS Blood Laboratory Management System API",
    status = "Online",
    version = "1.0.0",
    docs = "/openapi/v1.json"
}));

app.Run();
