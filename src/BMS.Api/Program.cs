using BMS.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddServiceCatalog(builder.Configuration);
builder.Services.AddBmsSwagger();
builder.Services.AddBmsCors();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseBmsSwagger();
}

app.UseCors("AllowFrontend");
app.MapControllers();
app.Run();