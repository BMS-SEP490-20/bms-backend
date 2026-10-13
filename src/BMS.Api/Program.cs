using BMS.Api.Extensions;
using BMS.Application;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Infrastructure;
using BMS.Infrastructure.Data;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// =========================================================
// SERVICES
// =========================================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

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

var app = builder.Build();

// Forwarded headers should run early in the pipeline.
app.UseForwardedHeaders();

// Exception handling
if (app.Environment.IsDevelopment())
{
    app.UseBmsSwagger();
}
else
{
    app.UseExceptionHandler();
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
