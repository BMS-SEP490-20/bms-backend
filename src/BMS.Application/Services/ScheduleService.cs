using BMS.Core.Entities;
using BMS.Schedule.DTOs;
using BMS.Schedule.Repositories;
using BMS.Application.Interfaces;
using System.Globalization;

namespace BMS.Application.Services;

public class ScheduleService : IScheduleService
{
    private readonly IScheduleRepository _repository;

    public ScheduleService(IScheduleRepository repository) => _repository = repository;

    public async Task<List<ShiftDto>> ListAsync(
        string doctorId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var list = await _repository.GetByDoctorAsync(doctorId, from, to, ct);
        return list.Select(Map).ToList();
    }

    public async Task<ShiftDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var s = await _repository.GetByIdAsync(id, ct);
        return s == null ? null : Map(s);
    }

    public async Task<ShiftDto> CreateAsync(CreateShiftRequest request, CancellationToken ct = default)
    {
        if (request.ShiftDate.Date < DateTime.UtcNow.Date)
            throw new InvalidOperationException("MSG-ERR-SCHED-01: Không tạo lịch trong quá khứ.");

        var overlap = await _repository.FindOverlapAsync(
            request.DoctorId, request.ShiftDate, request.ShiftType, ct);
        if (overlap != null)
            throw new InvalidOperationException(
                "MSG-ERR-SCHED-02: Đã có ca cùng loại trong ngày này. Hãy cập nhật ca hiện có.");

        // Default window from shift type when start/end omitted.
        var (start, end) = ResolveWindow(request.ShiftType, request.ShiftStart, request.ShiftEnd);
        await ValidateWindowAsync(request.DoctorId, request.ShiftDate, start, end, null, ct);

        var shift = new DoctorShift
        {
            DoctorId = request.DoctorId,
            EmployeeName = request.EmployeeName,
            EmployeeRole = "Doctor",
            Department = request.Department,
            RoomId = request.RoomId,
            ShiftDate = request.ShiftDate.Date,
            ShiftType = request.ShiftType,
            ShiftStart = start,
            ShiftEnd = end,
            MaxPatients = request.MaxPatients,
            Notes = request.Notes,
            Status = "Approved",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _repository.InsertAsync(shift, ct);
        return Map(shift);
    }

    public async Task<ShiftDto> UpdateAsync(string id, UpdateShiftRequest request, CancellationToken ct = default)
    {
        var shift = await _repository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("MSG-ERR-SCHED-03: Không tìm thấy ca làm việc.");

        if (request.ShiftType != null) shift.ShiftType = request.ShiftType;
        if (request.RoomId != null) shift.RoomId = request.RoomId;
        if (request.ShiftStart != null) shift.ShiftStart = request.ShiftStart;
        if (request.ShiftEnd != null) shift.ShiftEnd = request.ShiftEnd;
        if (request.MaxPatients.HasValue) shift.MaxPatients = request.MaxPatients.Value;
        if (request.Notes != null) shift.Notes = request.Notes;
        if (request.Status != null) shift.Status = request.Status;
        var (start, end) = ResolveWindow(shift.ShiftType, shift.ShiftStart, shift.ShiftEnd);
        await ValidateWindowAsync(shift.DoctorId, shift.ShiftDate, start, end, shift.Id, ct);
        shift.ShiftStart = start;
        shift.ShiftEnd = end;
        shift.UpdatedAt = DateTime.UtcNow;

        await _repository.ReplaceAsync(shift, ct);
        return Map(shift);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var shift = await _repository.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("MSG-ERR-SCHED-03: Không tìm thấy ca làm việc.");

        if (shift.CurrentPatientsCount > 0)
            throw new InvalidOperationException(
                "MSG-ERR-SCHED-04: Ca đã có bệnh nhân — hãy đặt status=Cancelled thay vì xóa.");

        await _repository.DeleteAsync(id, ct);
    }

    /// <summary>Rejects malformed, reversed and overlapping windows before persisting a personal shift.</summary>
    private async Task ValidateWindowAsync(string employee, DateTime day, string start, string end,
        string? excludeId, CancellationToken ct)
    {
        if (!TimeOnly.TryParseExact(start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            || !TimeOnly.TryParseExact(end, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to)
            || from >= to)
            throw new InvalidOperationException("MSG-ERR-SCHED-05: Giờ bắt đầu phải trước giờ kết thúc trong cùng ngày.");
        var existing = await _repository.GetByDoctorAsync(employee, day.Date, day.Date, ct);
        foreach (var item in existing.Where(x => x.Id != excludeId && x.Status is not ("Cancelled" or "Rejected")))
        {
            var window = ResolveWindow(item.ShiftType, item.ShiftStart, item.ShiftEnd);
            if (TimeOnly.TryParse(window.start, out var otherStart) && TimeOnly.TryParse(window.end, out var otherEnd)
                && from < otherEnd && to > otherStart)
                throw new InvalidOperationException("MSG-ERR-SCHED-02: Khoảng giờ trùng ca làm việc hiện có.");
        }
    }

    private static (string start, string end) ResolveWindow(string shiftType, string? start, string? end)
    {
        if (string.IsNullOrWhiteSpace(start) != string.IsNullOrWhiteSpace(end))
            throw new InvalidOperationException("MSG-ERR-SCHED-05: Cần cung cấp cả giờ bắt đầu và kết thúc.");
        if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end))
            return (start, end);

        return shiftType switch
        {
            "Afternoon" => ("13:30", "17:00"),
            "Evening" => ("17:00", "20:00"),
            "Night" => ("20:00", "23:00"),
            "FullDay" => ("08:00", "17:00"),
            _ => ("08:00", "12:00")
        };
    }

    private static ShiftDto Map(DoctorShift s) => new()
    {
        Id = s.Id,
        DoctorId = s.DoctorId,
        EmployeeName = s.EmployeeName,
        RoomId = s.RoomId,
        RoomName = s.RoomName,
        ShiftDate = s.ShiftDate,
        ShiftType = s.ShiftType,
        ShiftStart = s.ShiftStart,
        ShiftEnd = s.ShiftEnd,
        MaxPatients = s.MaxPatients,
        CurrentPatientsCount = s.CurrentPatientsCount,
        Status = s.Status,
        Notes = s.Notes
    };
}
