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
builder.Services.AddScoped<BMS.Core.Interfaces.ISampleListRepository, BMS.Infrastructure.Repositories.SampleListRepository>();
builder.Services.AddScoped<ISampleListService, SampleListService>();
builder.Services.AddScoped<BMS.Core.Interfaces.ITestReportRepository, BMS.Infrastructure.Repositories.TestReportRepository>();
builder.Services.AddScoped<ITestReportService, TestReportService>();
builder.Services.AddScoped<BMS.Core.Interfaces.ITestNoteRepository, BMS.Infrastructure.Repositories.TestNoteRepository>();
builder.Services.AddScoped<ITestNoteService, TestNoteService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IPatientResultExportService, PatientResultExportService>();
builder.Services.AddScoped<BMS.Core.Interfaces.IIotThresholdRepository, BMS.Infrastructure.Repositories.IotThresholdRepository>();
builder.Services.AddScoped<IIotThresholdService, IotThresholdService>();
builder.Services.AddScoped<BMS.Core.Interfaces.ITestResultVerificationRepository, BMS.Infrastructure.Repositories.TestResultVerificationRepository>();
builder.Services.AddScoped<ITestResultVerificationService, TestResultVerificationService>();
builder.Services.AddScoped<BMS.Core.Interfaces.ISampleOriginTraceRepository, BMS.Infrastructure.Repositories.SampleOriginTraceRepository>();
builder.Services.AddScoped<ISampleOriginTraceService, SampleOriginTraceService>();
builder.Services.AddScoped<BMS.Core.Interfaces.IMedicalAssessmentRepository, BMS.Infrastructure.Repositories.MedicalAssessmentRepository>();
builder.Services.AddScoped<IMedicalAssessmentService, MedicalAssessmentService>();
builder.Services.AddScoped<BMS.Core.Interfaces.IBackupRepository, BMS.Infrastructure.Repositories.BackupRepository>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<BMS.Core.Interfaces.ITestResultNotificationRepository, BMS.Infrastructure.Repositories.TestResultNotificationRepository>();
builder.Services.AddScoped<ITestResultNotificationService, TestResultNotificationService>();
// builder.Services.AddScoped<BMS.Core.Interfaces.ITestResultNotificationRepository, BMS.Infrastructure.Repositories.TestResultNotificationRepository>();
// builder.Services.AddScoped<ITestResultNotificationService, TestResultNotificationService>();



// IMPORTANT:
// KhÃƒÂ´ng Ã„â€˜Ã„Æ’ng kÃƒÂ½ IAuthService lÃ¡ÂºÂ§n nÃ¡Â»Â¯a Ã¡Â»Å¸ Ã„â€˜ÃƒÂ¢y nÃ¡ÂºÂ¿u
// AddAuthModule(), AddApplicationServices() hoÃ¡ÂºÂ·c
// AddInfrastructureServices() Ã„â€˜ÃƒÂ£ Ã„â€˜Ã„Æ’ng kÃƒÂ½ dÃ¡Â»â€¹ch vÃ¡Â»Â¥ nÃƒÂ y.


// =========================================================
// CORS
// =========================================================

// SignalR Real-Time Queue
builder.Services.AddSignalR();
builder.Services.AddScoped<IQueueNotifier, BMS.Api.Services.QueueNotifier>();

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
            .AllowAnyMethod()
            .AllowCredentials();
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

// Controllers & Hubs
app.MapControllers();
app.MapHub<BMS.Api.Hubs.QueueHub>("/hubs/queue");

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
