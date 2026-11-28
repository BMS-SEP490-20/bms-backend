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
    private readonly IMongoCollection<BsonDocument> _medicalAssessments;

    public PatientRepository(IMongoDatabase db)
    {
        _patients = db.GetCollection<Patient>("patients");
        _appointments = db.GetCollection<BsonDocument>("appointments");
        _medicalRecords = db.GetCollection<BsonDocument>("medical_records");
        _medicalAssessments = db.GetCollection<BsonDocument>("medical_assessments");
    }

    /// <summary>Indexes for reception lookups (INV-5): unique CCCD among string values, unique AccountId among non-empty values, phone for duplicate checks.</summary>
    public static IEnumerable<CreateIndexModel<Patient>> Indexes() =>
    [
        new(Builders<Patient>.IndexKeys.Ascending(p => p.IdentityCardNumber),
            new CreateIndexOptions<Patient>
            {
                Name = "ux_patients_cccd",
                Unique = true,
                PartialFilterExpression = Builders<Patient>.Filter.Type(p => p.IdentityCardNumber, MongoDB.Bson.BsonType.String)
            }),
        new(Builders<Patient>.IndexKeys.Ascending(p => p.AccountId),
            new CreateIndexOptions<Patient>
            {
                Name = "ux_patients_account_id",
                Unique = true,
                PartialFilterExpression = Builders<Patient>.Filter.Gt(p => p.AccountId, "")
            }),
        new(Builders<Patient>.IndexKeys.Ascending(p => p.PhoneNumber), new CreateIndexOptions { Name = "ix_patients_phone" }),
    ];

    public async Task<Patient?> GetByIdOrCodeAsync(string idOrCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            return null;

        var trimmed = idOrCode.Trim();
        var fb = Builders<Patient>.Filter;

        var stripped = trimmed.StartsWith("PAT-", StringComparison.OrdinalIgnoreCase)
            ? System.Text.RegularExpressions.Regex.Replace(trimmed, "^(PAT-)+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            : trimmed;

        var filter = fb.Or(
            fb.Eq(p => p.Id, trimmed),
            fb.Eq(p => p.Id, stripped),
            fb.Eq(p => p.Id, $"PAT-{stripped}"),
            fb.Eq(p => p.PatientCodeField, trimmed),
            fb.Eq(p => p.PatientCodeField, stripped),
            fb.Eq(p => p.IdentityCardNumber, trimmed),
            fb.Eq(p => p.IdentityCardNumber, stripped));

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
            throw new PatientDuplicateException(
                "MSG-ERR-REC-01",
                $"Số CCCD {BMS.Core.Utilities.PiiMasker.Mask(patient.IdentityCardNumber)} đã có hồ sơ.");
        }
    }

    public async Task UpdateAsync(Patient patient, CancellationToken ct = default)
    {
        patient.UpdatedAt = DateTime.UtcNow;
        var filter = Builders<Patient>.Filter.Eq(p => p.Id, patient.Id);
        try
        {
            await _patients.ReplaceOneAsync(filter, patient, new ReplaceOptions { IsUpsert = false }, ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new PatientDuplicateException(
                "MSG-ERR-REC-01",
                $"Thông tin định danh hoặc tài khoản đã tồn tại hồ sơ bệnh nhân khác.");
        }
    }

    public async Task<IReadOnlyList<PatientAppointmentSummaryDto>> GetRecentAppointmentsAsync(
        string patientId,
        int limit = 10,
        CancellationToken ct = default)
    {
        try
        {
            var filter = Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("patientId", patientId),
                Builders<BsonDocument>.Filter.Eq("patient_id", patientId),
                Builders<BsonDocument>.Filter.Eq("PatientId", patientId));

            var docs = await _appointments
                .Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("appointmentDate"))
                .Limit(limit)
                .ToListAsync(ct);

            var list = new List<PatientAppointmentSummaryDto>(docs.Count);
            foreach (var doc in docs)
            {
                var packages = new List<string>();
                foreach (var key in new[] { "test_packages", "testPackages" })
                {
                    if (doc.TryGetValue(key, out var pkgsVal) && pkgsVal.IsBsonArray)
                    {
                        foreach (var item in pkgsVal.AsBsonArray)
                        {
                            if (item.IsString) packages.Add(item.AsString);
                        }
                    }
                }

                var code = ReadString(doc, "appointmentCode", "appointment_code", "AppointmentCode");
                var id = ReadString(doc, "_id") ?? string.Empty;

                var pkgName = ReadString(doc, "packageName", "testPackageName", "PackageName", "packageCode");
                var start = ReadDate(doc, "startTime", "appointmentTime", "appointmentDate", "appointment_date");
                var slot = ReadString(doc, "slotTime", "timeSlot");
                if (string.IsNullOrWhiteSpace(slot) && start.HasValue)
                    slot = start.Value.ToLocalTime().ToString("HH:mm");

                list.Add(new PatientAppointmentSummaryDto
                {
                    AppointmentId = !string.IsNullOrWhiteSpace(code) ? code! : id,
                    AppointmentDate = ReadDate(doc, "appointmentDate", "appointment_date", "appointmentTime") ?? start,
                    Type = ReadString(doc, "type", "Type"),
                    TimeSlot = slot,
                    Status = ReadString(doc, "status", "Status"),
                    TotalAmount = ReadDecimal(doc, "totalAmount", "total_amount", "paidAmount"),
                    PaymentStatus = ReadString(doc, "paymentStatus", "payment_status"),
                    ServicePackageName = pkgName,
                    TestPackages = packages.Count > 0 ? packages : (string.IsNullOrWhiteSpace(pkgName) ? Array.Empty<string>() : new[] { pkgName! })
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
            var filter = Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Eq("patientId", patientId),
                Builders<BsonDocument>.Filter.Eq("patient_id", patientId),
                Builders<BsonDocument>.Filter.Eq("PatientId", patientId));

            var records = await _medicalRecords
                .Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("createdAt"))
                .Limit(limit)
                .ToListAsync(ct);

            var assessments = await _medicalAssessments
                .Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("created_at"))
                .Limit(limit)
                .ToListAsync(ct);

            var list = new List<PatientMedicalRecordSummaryDto>();
            foreach (var doc in records.Concat(assessments))
            {
                list.Add(new PatientMedicalRecordSummaryDto
                {
                    RecordId = ReadString(doc, "assessment_code", "recordId", "RecordId", "_id") ?? string.Empty,
                    AppointmentId = ReadString(doc, "appointment_id", "appointmentId"),
                    DoctorId = ReadString(doc, "doctor_id", "doctorId"),
                    DoctorName = ReadString(doc, "doctor_name", "doctorName"),
                    Symptoms = ReadString(doc, "symptoms", "Symptoms"),
                    Diagnosis = ReadString(doc, "diagnosis", "Diagnosis", "preliminary_diagnosis"),
                    DoctorNotes = ReadString(doc, "doctor_notes", "doctorNotes", "clinical_notes"),
                    ClinicalNotes = ReadString(doc, "clinical_notes", "clinicalNotes", "notes"),
                    Recommendations = ReadString(doc, "recommendations", "Recommendations"),
                    VisitDate = ReadDate(doc, "visit_date", "visitDate", "created_at", "createdAt"),
                    CreatedAt = ReadDate(doc, "created_at", "createdAt", "visit_date")
                });
            }

            return list
                .OrderByDescending(x => x.VisitDate ?? x.CreatedAt ?? DateTime.MinValue)
                .Take(limit)
                .ToList();
        }
        catch
        {
            return Array.Empty<PatientMedicalRecordSummaryDto>();
        }
    }

    private static string? ReadString(BsonDocument doc, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (doc.TryGetValue(key, out var val) && !val.IsBsonNull)
            {
                if (val.IsString) return val.AsString;
                return val.ToString();
            }
        }
        return null;
    }

    private static DateTime? ReadDate(BsonDocument doc, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (doc.TryGetValue(key, out var val) && val.IsValidDateTime)
                return val.ToUniversalTime();
        }
        return null;
    }

    private static decimal? ReadDecimal(BsonDocument doc, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (doc.TryGetValue(key, out var val) && val.IsNumeric)
                return val.ToDecimal();
        }
        return null;
    }
}
