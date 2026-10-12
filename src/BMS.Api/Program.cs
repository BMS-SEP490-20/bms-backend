
using BMS.Api.Extensions;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMongo(builder.Configuration);
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddPasswordRecovery(builder.Configuration, builder.Environment);
builder.Services.AddServicePackages();
builder.Services.AddTestCriteria();
builder.Services.AddApiHardening(builder.Configuration);

// CORS cho React/Vite frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("BmsFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Forwarded headers
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// Phải dùng đúng policy đã đăng ký
app.UseCors("BmsFrontend");

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();