using BMS.Core.Entities;

namespace BMS.Schedule.Repositories;

public interface IScheduleRepository
{
    Task<DoctorShift?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<DoctorShift>> GetByDoctorAsync(string doctorId, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<DoctorShift?> FindOverlapAsync(string doctorId, DateTime date, string shiftType, CancellationToken ct = default);
    Task InsertAsync(DoctorShift shift, CancellationToken ct = default);
    Task ReplaceAsync(DoctorShift shift, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}
