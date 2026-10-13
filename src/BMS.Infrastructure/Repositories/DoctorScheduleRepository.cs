using BMS.Application.Interfaces;
using BMS.Core.Entities;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB repository for doctor schedule collections (UC16).
/// </summary>
public class DoctorScheduleRepository : IDoctorScheduleRepository
{
    private readonly IMongoCollection<Doctor> _doctors;
    private readonly IMongoCollection<DoctorShift> _shifts;
    private readonly IMongoCollection<DoctorRoom> _rooms;

    public DoctorScheduleRepository(IMongoDatabase database)
    {
        _doctors = database.GetCollection<Doctor>("doctors");
        _shifts = database.GetCollection<DoctorShift>("doctor_shifts");
        _rooms = database.GetCollection<DoctorRoom>("doctor_rooms");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Doctor>> GetDoctorsAsync(
        string? search = null, CancellationToken ct = default)
    {
        var filter = Builders<Doctor>.Filter.Empty;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var kw = search.Trim();
            filter = Builders<Doctor>.Filter.Or(
                Builders<Doctor>.Filter.Regex(d => d.FullName,
                    new MongoDB.Bson.BsonRegularExpression(kw, "i")),
                Builders<Doctor>.Filter.Regex(d => d.Specialty,
                    new MongoDB.Bson.BsonRegularExpression(kw, "i"))
            );
        }

        return await _doctors.Find(filter).SortBy(d => d.FullName).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<Doctor?> GetDoctorByIdAsync(string doctorId, CancellationToken ct = default)
    {
        return await _doctors.Find(d => d.Id == doctorId).FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DoctorShift>> GetShiftsByDateAsync(
        DateTime date, string? doctorId = null, CancellationToken ct = default)
    {
        var dayStart = date.Date;
        var dayEnd = dayStart.AddDays(1);

        var filter = Builders<DoctorShift>.Filter.And(
            Builders<DoctorShift>.Filter.Gte(s => s.ShiftDate, dayStart),
            Builders<DoctorShift>.Filter.Lt(s => s.ShiftDate, dayEnd)
        );

        if (!string.IsNullOrWhiteSpace(doctorId))
            filter &= Builders<DoctorShift>.Filter.Eq(s => s.DoctorId, doctorId);

        return await _shifts.Find(filter).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, DoctorRoom>> GetRoomsMapAsync(
        CancellationToken ct = default)
    {
        var list = await _rooms.Find(r => r.IsActive).ToListAsync(ct);
        return list.ToDictionary(r => r.Id);
    }
}