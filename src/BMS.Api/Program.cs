using BMS.Application;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Infrastructure;
using BMS.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Register Clean Architecture layers for MongoDB & Identity / Lab Criteria
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices();

// Register In-Memory Context as Singleton for dev state persistence
builder.Services.AddSingleton<BmsDbContext>();

// Register Application Services from main
builder.Services.AddScoped<ILabQueueService, LabQueueService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Configure CORS for frontend applications (React on 3000, Vite on 5173, Mobile apps)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

// Health Check endpoint
app.MapGet("/", () => Results.Ok(new
{
    system = "BMS Blood Laboratory Management System API",
    status = "Online",
    version = "1.0.0",
    docs = "/openapi/v1.json"
}));

app.Run();
