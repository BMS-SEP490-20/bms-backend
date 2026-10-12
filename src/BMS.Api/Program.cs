using BMS.Api;
using BMS.Api.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// Shared (once)
builder.Services.AddBmsInfrastructure(builder.Configuration);
builder.Services.AddBmsSwagger();
builder.Services.AddBmsCors();

// Features
builder.Services.AddServiceCatalog();
builder.Services.AddDoctorSchedule();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseBmsSwagger();

app.UseCors("AllowFrontend");
app.MapControllers();
app.Run();