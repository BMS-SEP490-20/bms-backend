using BMS.Core.Entities;
using MongoDB.Driver;

namespace BMS.Schedule.Repositories;

public class ScheduleRepository : IScheduleRepository
{
    private readonly IMongoCollection<DoctorShift> _collection;

    public ScheduleRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<DoctorShift>("doctor_shifts");
        var idx = Builders<DoctorShift>.IndexKeys
            .Ascending(x => x.DoctorId)
            .Ascending(x => x.ShiftDate)
            .Ascending(x => x.ShiftType);
        _collection.Indexes.CreateOne(new CreateIndexModel<DoctorShift>(idx));
    }

    public async Task<DoctorShift?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _collection.Find(x => x.Id == id).FirstOrDefaultAsync(ct);

    public async Task<List<DoctorShift>> GetByDoctorAsync(
        string doctorId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var filter = Builders<DoctorShift>.Filter.Eq(x => x.DoctorId, doctorId);
        if (from.HasValue)
            filter &= Builders<DoctorShift>.Filter.Gte(x => x.ShiftDate, from.Value.Date);
        if (to.HasValue)
            filter &= Builders<DoctorShift>.Filter.Lt(x => x.ShiftDate, to.Value.Date.AddDays(1));
        return await _collection.Find(filter).SortBy(x => x.ShiftDate).ToListAsync(ct);
    }

    public async Task<DoctorShift?> FindOverlapAsync(
        string doctorId, DateTime date, string shiftType, CancellationToken ct = default)
    {
        var start = date.Date;
        var end = start.AddDays(1);
        return await _collection.Find(x =>
            x.DoctorId == doctorId
            && x.ShiftDate >= start
            && x.ShiftDate < end
            && x.ShiftType == shiftType
            && x.Status != "Cancelled"
            && x.Status != "Rejected").FirstOrDefaultAsync(ct);
    }

    public async Task InsertAsync(DoctorShift shift, CancellationToken ct = default)
        => await _collection.InsertOneAsync(shift, cancellationToken: ct);

    public async Task ReplaceAsync(DoctorShift shift, CancellationToken ct = default)
        => await _collection.ReplaceOneAsync(x => x.Id == shift.Id, shift, cancellationToken: ct);

    public async Task DeleteAsync(string id, CancellationToken ct = default)
        => await _collection.DeleteOneAsync(x => x.Id == id, ct);
}
