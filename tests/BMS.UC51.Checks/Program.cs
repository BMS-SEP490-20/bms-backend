using BMS.Api.Controllers.Clinic;
using BMS.Application.DTOs.Clinic;
using BMS.Application.DTOs.MedicalRecords;
using BMS.Application.Interfaces;
using BMS.Application.Services;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BMS.Api.Middleware;

// Dependency-free behavioral checks; no production database is read or written.
var records = new Records();
var notes = new Notes();
var service = new MedicalNoteService(records, notes);
var passed = 0;
await Check("creates trimmed addendum with server-derived patient and author", async () =>
{
    var dto = Request("  Diễn biến mới\nLời dặn  ");
    var result = await service.AddAsync(dto, "doctor-1", "Bác sĩ A");
    Assert(result.Content == "Diễn biến mới\nLời dặn" && result.AuthorName == "Bác sĩ A");
    Assert(notes.Stored[result.Id].PatientId == "patient-1" && notes.Stored[result.Id].AuthorId == "doctor-1");
    Assert(result.CreatedAt.Kind == DateTimeKind.Utc && records.Record.Diagnosis == "Existing diagnosis");
});
await Check("unchanged retry returns the original note", async () =>
{
    var dto = Request("Theo dõi");
    var first = await service.AddAsync(dto, "doctor-1", "Bác sĩ A");
    var count = notes.Stored.Count;
    var second = await service.AddAsync(dto, "doctor-1", "Bác sĩ A");
    Assert(first == second && notes.Stored.Count == count);
});
await Check("reused submission ID rejects changed content", async () =>
{
    var dto = Request("Original");
    await service.AddAsync(dto, "doctor-1", "A");
    dto.Content = "Changed";
    await Reject<MedicalNoteConflictException>(() => service.AddAsync(dto, "doctor-1", "A"));
});
await Check("another doctor cannot add or read notes", async () =>
{
    var count = notes.Stored.Count;
    await Reject<ForbiddenAccessException>(() => service.AddAsync(Request("Private"), "doctor-2", "B"));
    await Reject<ForbiddenAccessException>(() => service.GetContextAsync("EMR-1", "doctor-2", 1, 20));
    Assert(notes.Stored.Count == count);
});
await Check("missing doctor identity is rejected", () =>
    Reject<ForbiddenAccessException>(() => service.GetContextAsync("EMR-1", "", 1, 20)));
await Check("missing or deleted EMR is rejected", async () =>
{
    await Reject<MedicalNoteRecordNotFoundException>(() => service.GetContextAsync("missing", "doctor-1", 1, 20));
    var dto = Request("Note"); dto.RecordId = "deleted";
    await Reject<MedicalNoteRecordNotFoundException>(() => service.AddAsync(dto, "doctor-1", "A"));
});
await Check("blank and oversized notes are rejected", async () =>
{
    await Reject<DomainValidationException>(() => service.AddAsync(Request(" \n "), "doctor-1", "A"));
    await Reject<DomainValidationException>(() => service.AddAsync(Request(new string('x', 5001)), "doctor-1", "A"));
});
await Check("5000 characters are accepted", async () =>
    Assert((await service.AddAsync(Request(new string('x', 5000)), "doctor-1", "A")).Content.Length == 5000));
await Check("empty submission ID is rejected", async () =>
{
    var dto = Request("Note"); dto.SubmissionId = Guid.Empty;
    await Reject<DomainValidationException>(() => service.AddAsync(dto, "doctor-1", "A"));
});
await Check("bounded history uses canonical record ID", async () =>
{
    var context = await service.GetContextAsync("EMR-1", "doctor-1", 1, 2);
    Assert(context.Items.Count == 2 && context.Total == notes.Stored.Count && notes.LastRecordId == "record-1");
    await Reject<DomainValidationException>(() => service.GetContextAsync("EMR-1", "doctor-1", 0, 20));
    await Reject<DomainValidationException>(() => service.GetContextAsync("EMR-1", "doctor-1", 1, 51));
});
await Check("controller requires authenticated Doctor role", () =>
{
    var attribute = typeof(MedicalNotesController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
        .Cast<AuthorizeAttribute>().Single();
    Assert(attribute.Roles == "Doctor");
    return Task.CompletedTask;
});
await Check("HTTP contracts enforce authentication, role, validation, and persistence", async () =>
{
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    builder.Services.AddControllers().AddApplicationPart(typeof(MedicalNotesController).Assembly);
    builder.Services.AddSingleton<IMedicalRecordService>(records);
    builder.Services.AddSingleton<IMedicalNoteRepository>(notes);
    builder.Services.AddScoped<IMedicalNoteService, MedicalNoteService>();
    builder.Services.AddAuthentication("UC51Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("UC51Test", _ => { });
    builder.Services.AddAuthorization();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();
    await using var app = builder.Build();
    app.UseExceptionHandler();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    await app.StartAsync();
    using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    Assert((await http.GetAsync("/api/medical-notes?recordId=EMR-1")).StatusCode == HttpStatusCode.Unauthorized);
    http.DefaultRequestHeaders.Add("X-Test-Role", "Patient");
    Assert((await http.GetAsync("/api/medical-notes?recordId=EMR-1")).StatusCode == HttpStatusCode.Forbidden);
    http.DefaultRequestHeaders.Remove("X-Test-Role");
    http.DefaultRequestHeaders.Add("X-Test-Role", "Doctor");
    Assert((await http.GetAsync("/api/medical-notes")).StatusCode == HttpStatusCode.BadRequest);
    Assert((await http.GetAsync("/api/medical-notes?recordId=EMR-1&pageSize=51")).StatusCode == HttpStatusCode.BadRequest);
    Assert((await http.GetAsync("/api/medical-notes?recordId=missing")).StatusCode == HttpStatusCode.NotFound);
    Assert((await http.PostAsJsonAsync("/api/medical-notes", Request("   "))).StatusCode == HttpStatusCode.BadRequest);
    var dto = Request("HTTP integration note");
    var response = await http.PostAsJsonAsync("/api/medical-notes", dto);
    Assert(response.StatusCode == HttpStatusCode.Created && response.Headers.Location != null);
    var created = await response.Content.ReadFromJsonAsync<MedicalNoteDto>();
    var retry = await http.PostAsJsonAsync("/api/medical-notes", dto);
    Assert((await retry.Content.ReadFromJsonAsync<MedicalNoteDto>())?.Id == created?.Id);
    Assert((await http.GetAsync(response.Headers.Location)).StatusCode == HttpStatusCode.OK);
    dto.Content = "Conflicting retry";
    Assert((await http.PostAsJsonAsync("/api/medical-notes", dto)).StatusCode == HttpStatusCode.Conflict);
    http.DefaultRequestHeaders.Add("X-Test-Doctor", "doctor-2");
    Assert((await http.GetAsync("/api/medical-notes?recordId=EMR-1")).StatusCode == HttpStatusCode.Forbidden);
    await app.StopAsync();
});
Console.WriteLine($"UC51: {passed} checks passed.");

CreateMedicalNoteDto Request(string content) => new() { RecordId = "EMR-1", Content = content, SubmissionId = Guid.NewGuid() };
async Task Check(string name, Func<Task> action) { await action(); passed++; Console.WriteLine($"PASS {name}"); }
static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
static async Task Reject<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

sealed class Records : IMedicalRecordService
{
    public MedicalRecordDto Record { get; } = new()
    {
        Id = "record-1", RecordCode = "EMR-1", DoctorId = "doctor-1", PatientId = "patient-1",
        PatientName = "Patient", DoctorName = "Doctor", Diagnosis = "Existing diagnosis"
    };
    public Task<MedicalRecordDto?> GetByIdAsync(string id, string? user, string? role, CancellationToken ct = default) =>
        Task.FromResult<MedicalRecordDto?>(id is "EMR-1" or "record-1" ? Record : null);
    public Task<List<MedicalRecordDto>> GetByPatientIdAsync(string id, string? user, string? role, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<MedicalRecordDto> CreateAsync(CreateMedicalRecordDto dto, string doctor, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<MedicalRecordDto> UpdateAsync(string id, UpdateMedicalRecordDto dto, string doctor, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(string id, DeleteMedicalRecordRequest dto, string doctor, CancellationToken ct = default) => throw new NotSupportedException();
}

sealed class Notes : IMedicalNoteRepository
{
    public Dictionary<string, MedicalNote> Stored { get; } = new();
    public string? LastRecordId { get; private set; }
    public Task<MedicalNote> InsertOrGetAsync(MedicalNote note, CancellationToken ct = default)
    {
        Stored.TryAdd(note.Id, note);
        return Task.FromResult(Stored[note.Id]);
    }
    public Task<(List<MedicalNote> Items, long Total)> ListAsync(string id, int page, int size, CancellationToken ct = default)
    {
        LastRecordId = id;
        var all = Stored.Values.Where(n => n.RecordId == id).OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).ToList();
        return Task.FromResult((all.Skip((page - 1) * size).Take(size).ToList(), (long)all.Count));
    }
}

// This scheme exists only in the test executable, never in the application host.
sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var role = Request.Headers["X-Test-Role"].ToString();
        if (string.IsNullOrEmpty(role)) return Task.FromResult(AuthenticateResult.NoResult());
        var doctor = Request.Headers["X-Test-Doctor"].FirstOrDefault() ?? "doctor-1";
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, doctor),
            new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.Name, "Test Doctor") }, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
