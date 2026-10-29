using System.Text.RegularExpressions;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Patients;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB repository for the <c>patients</c> collection and related clinical history.
/// </summary>
public sealed class PatientRepository : IPatientRepository
{
    private readonly IMongoCollection<Patient> _patients;
    private readonly IMongoCollection<BsonDocument> _appointments;
    private readonly IMongoCollection<BsonDocument> _medicalRecords;

    public PatientRepository(IMongoDatabase db)
    {
        _patients = db.GetCollection<Patient>("patients");
        _appointments = db.GetCollection<BsonDocument>("appointments");
        _medicalRecords = db.GetCollection<BsonDocument>("medical_records");
    }

    /// <summary>Indexes for reception lookups (INV-5): unique CCCD among string values, phone for duplicate checks.</summary>
    public static IEnumerable<CreateIndexModel<Patient>> Indexes() =>
    [
        new(Builders<Patient>.IndexKeys.Ascending(p => p.IdentityCardNumber),
            new CreateIndexOptions<Patient>
            {
                Name = "ux_patients_cccd",
                Unique = true,
                PartialFilterExpression = Builders<Patient>.Filter.Type(p => p.IdentityCardNumber, MongoDB.Bson.BsonType.String)
            }),
        new(Builders<Patient>.IndexKeys.Ascending(p => p.PhoneNumber), new CreateIndexOptions { Name = "ix_patients_phone" }),
    ];

    public async Task<Patient?> GetByIdOrCodeAsync(string idOrCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            return null;

        var trimmed = idOrCode.Trim();
        var fb = Builders<Patient>.Filter;

        var filter = fb.Or(
            fb.Eq(p => p.Id, trimmed),
            fb.Eq(p => p.PatientCodeField, trimmed),
            fb.Eq(p => p.IdentityCardNumber, trimmed));

        return await _patients.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<Patient?> GetByAccountIdAsync(string accountId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return null;

        var trimmed = accountId.Trim();
        var fb = Builders<Patient>.Filter;

        var filters = new List<FilterDefinition<Patient>>
        {
            fb.Eq("AccountId", trimmed),
            fb.Eq("userId", trimmed)
        };

        if (ObjectId.TryParse(trimmed, out var oid))
        {
            filters.Add(fb.Eq("AccountId", oid));
        }

        return await _patients.Find(fb.Or(filters)).FirstOrDefaultAsync(ct);
    }

    public async Task<Patient?> FindByIdentityNumberAsync(string identityNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identityNumber))
            return null;

        var trimmed = identityNumber.Trim();
        var fb = Builders<Patient>.Filter;
        var filter = fb.Or(
            fb.Eq(p => p.IdentityCardNumber, trimmed),
            fb.Eq(p => p.Id, $"PAT-{trimmed}"));

        return await _patients.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<Patient?> FindDuplicateGuestAsync(
        string fullName,
        string phoneNumber,
        DateTime? dateOfBirth,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(phoneNumber))
            return null;

        var fb = Builders<Patient>.Filter;
        var nameRx = new BsonRegularExpression($"^{Regex.Escape(fullName.Trim())}$", "i");
        var filter = fb.And(
            fb.Regex(p => p.FullName, nameRx),
            fb.Eq(p => p.PhoneNumber, phoneNumber.Trim()));

        if (dateOfBirth is { } dob)
        {
            var dayStart = dob.Date;
            var dayEnd = dayStart.AddDays(1);
            filter = fb.And(filter, fb.Gte(p => p.DateOfBirth, dayStart), fb.Lt(p => p.DateOfBirth, dayEnd));
        }

        return await _patients.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<PagedResult<Patient>> SearchAsync(PatientSearchRequest request, CancellationToken ct = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        var fb = Builders<Patient>.Filter;
        var filters = new List<FilterDefinition<Patient>>();

        if (!string.IsNullOrWhiteSpace(request.Gender))
        {
            var gRx = new BsonRegularExpression($"^{Regex.Escape(request.Gender.Trim())}$", "i");
            filters.Add(fb.Regex(p => p.GenderString, gRx));
        }

        if (!string.IsNullOrWhiteSpace(request.BloodType))
        {
            var bRx = new BsonRegularExpression($"^{Regex.Escape(request.BloodType.Trim())}$", "i");
            filters.Add(fb.Regex(p => p.BloodType, bRx));
        }

        if (request.IsGuest.HasValue)
        {
            if (request.IsGuest.Value)
            {
                filters.Add(fb.Or(
                    fb.Eq(p => p.IsGuest, true),
                    fb.Eq("AccountId", BsonNull.Value)));
            }
            else
            {
                filters.Add(fb.And(
                    fb.Ne(p => p.IsGuest, true),
                    fb.Ne("AccountId", BsonNull.Value)));
            }
        }

        var keyword = request.EffectiveKeyword;
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var rx = new BsonRegularExpression(Regex.Escape(keyword), "i");
            filters.Add(fb.Or(
                fb.Regex(p => p.Id, rx),
                fb.Regex(p => p.PatientCodeField, rx),
                fb.Regex(p => p.FullName, rx),
                fb.Regex(p => p.PhoneNumber, rx),
                fb.Regex(p => p.IdentityCardNumber, rx),
                fb.Regex(p => p.Email, rx)));
        }

        var finalFilter = filters.Count > 0 ? fb.And(filters) : FilterDefinition<Patient>.Empty;
        var total = await _patients.CountDocumentsAsync(finalFilter, cancellationToken: ct);

        var items = await _patients
            .Find(finalFilter)
            .SortByDescending(p => p.CreatedAt)
            .ThenBy(p => p.FullName)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Patient>(items, page, pageSize, total);
    }

    public async Task InsertAsync(Patient patient, CancellationToken ct = default)
    {
        try
        {
            await _patients.InsertOneAsync(patient, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // WHY masked + MSG-ERR-REC-01: a parallel registration with the same CCCD loses on the unique index
            // (ux_patients_cccd) and must see the same business error as the sequential pre-check (INV-4).
            throw new PatientDuplicateException(
                "MSG-ERR-REC-01",
                $"Số CCCD {BMS.Core.Utilities.PiiMasker.Mask(patient.IdentityCardNumber)} đã có hồ sơ.");
        }
    }

    public async Task<IReadOnlyList<PatientAppointmentSummaryDto>> GetRecentAppointmentsAsync(
        string patientId,
        int limit = 10,
        CancellationToken ct = default)
    {
        try
        {
            var docs = await _appointments
                .Find(Builders<BsonDocument>.Filter.Eq("patient_id", patientId))
                .Sort(Builders<BsonDocument>.Sort.Descending("appointment_date"))
                .Limit(limit)
                .ToListAsync(ct);

            var list = new List<PatientAppointmentSummaryDto>(docs.Count);
            foreach (var doc in docs)
            {
                var packages = new List<string>();
                if (doc.TryGetValue("test_packages", out var pkgsVal) && pkgsVal.IsBsonArray)
                {
                    foreach (var item in pkgsVal.AsBsonArray)
                    {
                        if (item.IsString) packages.Add(item.AsString);
                    }
                }

                list.Add(new PatientAppointmentSummaryDto
                {
                    AppointmentId = doc.GetValue("_id", BsonNull.Value).ToString() ?? string.Empty,
                    AppointmentDate = doc.TryGetValue("appointment_date", out var dt) && dt.IsValidDateTime ? dt.ToUniversalTime() : null,
                    Type = doc.TryGetValue("type", out var tp) && tp.IsString ? tp.AsString : null,
                    Status = doc.TryGetValue("status", out var st) && st.IsString ? st.AsString : null,
                    TotalAmount = doc.TryGetValue("total_amount", out var ta) && ta.IsNumeric ? ta.ToDecimal() : null,
                    PaymentStatus = doc.TryGetValue("payment_status", out var ps) && ps.IsString ? ps.AsString : null,
                    TestPackages = packages
                });
            }

            return list;
        }
        catch
        {
            return Array.Empty<PatientAppointmentSummaryDto>();
        }
    }

    public async Task<IReadOnlyList<PatientMedicalRecordSummaryDto>> GetRecentMedicalRecordsAsync(
        string patientId,
        int limit = 10,
        CancellationToken ct = default)
    {
        try
        {
            var docs = await _medicalRecords
                .Find(Builders<BsonDocument>.Filter.Eq("patient_id", patientId))
                .Sort(Builders<BsonDocument>.Sort.Descending("created_at"))
                .Limit(limit)
                .ToListAsync(ct);

            var list = new List<PatientMedicalRecordSummaryDto>(docs.Count);
            foreach (var doc in docs)
            {
                list.Add(new PatientMedicalRecordSummaryDto
                {
                    RecordId = doc.GetValue("_id", BsonNull.Value).ToString() ?? string.Empty,
                    AppointmentId = doc.TryGetValue("appointment_id", out var app) && !app.IsBsonNull ? app.ToString() : null,
                    DoctorId = doc.TryGetValue("doctor_id", out var docId) && !docId.IsBsonNull ? docId.ToString() : null,
                    Symptoms = doc.TryGetValue("symptoms", out var sym) && sym.IsString ? sym.AsString : null,
                    Diagnosis = doc.TryGetValue("diagnosis", out var diag) && diag.IsString ? diag.AsString : null,
                    DoctorNotes = doc.TryGetValue("doctor_notes", out var notes) && notes.IsString ? notes.AsString : null,
                    CreatedAt = doc.TryGetValue("created_at", out var ca) && ca.IsValidDateTime ? ca.ToUniversalTime() : null
                });
            }

            return list;
        }
        catch
        {
            return Array.Empty<PatientMedicalRecordSummaryDto>();
        }
    }
}
