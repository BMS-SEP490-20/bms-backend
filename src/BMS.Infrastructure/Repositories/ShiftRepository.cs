using System.Text.RegularExpressions;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Shifts;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BMS.Infrastructure.Repositories;

/// <summary>
/// MongoDB repository for the <c>doctor_shifts</c> collection and employee/room lookup enrichment.
/// </summary>
public sealed class ShiftRepository : IShiftRepository
{
    private readonly IMongoCollection<DoctorShift> _shifts;
    private readonly IMongoCollection<Doctor> _doctors;
    private readonly IMongoCollection<User> _accounts;
    private readonly IMongoCollection<DoctorRoom> _doctorRooms;
    private readonly IMongoCollection<BsonDocument> _labRooms;
    private readonly IMongoCollection<BsonDocument> _appointments;

    public ShiftRepository(IMongoDatabase db)
    {
        _shifts = db.GetCollection<DoctorShift>("doctor_shifts");
        _doctors = db.GetCollection<Doctor>("doctors");
        _accounts = db.GetCollection<User>("accounts");
        _doctorRooms = db.GetCollection<DoctorRoom>("doctor_rooms");
        _labRooms = db.GetCollection<BsonDocument>("lab_rooms");
        _appointments = db.GetCollection<BsonDocument>("appointments");
    }

    public async Task<DoctorShift?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var trimmed = id.Trim();
        return await _shifts.Find(Builders<DoctorShift>.Filter.Eq(s => s.Id, trimmed))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<PagedResult<DoctorShift>> SearchAsync(ShiftFilterRequest filter, CancellationToken ct = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var fb = Builders<DoctorShift>.Filter;
        var filters = new List<FilterDefinition<DoctorShift>>();

        if (filter.Date is { } exactDate)
        {
            var start = DateTime.SpecifyKind(exactDate.Date, DateTimeKind.Utc);
            var end = start.AddDays(1);
            filters.Add(fb.Gte(s => s.ShiftDate, start));
            filters.Add(fb.Lt(s => s.ShiftDate, end));
        }
        else
        {
            if (filter.FromDate is { } from)
            {
                var start = DateTime.SpecifyKind(from.Date, DateTimeKind.Utc);
                filters.Add(fb.Gte(s => s.ShiftDate, start));
            }

            if (filter.ToDate is { } to)
            {
                var end = DateTime.SpecifyKind(to.Date, DateTimeKind.Utc).AddDays(1);
                filters.Add(fb.Lt(s => s.ShiftDate, end));
            }
        }

        var empId = filter.EffectiveEmployeeId;
        if (!string.IsNullOrWhiteSpace(empId))
        {
            filters.Add(fb.Eq(s => s.DoctorId, empId));
        }

        if (!string.IsNullOrWhiteSpace(filter.RoomId))
        {
            var roomRx = new BsonRegularExpression($"^{Regex.Escape(filter.RoomId.Trim())}$", "i");
            filters.Add(fb.Regex(s => s.RoomId, roomRx));
        }

        if (!string.IsNullOrWhiteSpace(filter.ShiftType))
        {
            var typeRx = new BsonRegularExpression($"^{Regex.Escape(filter.ShiftType.Trim())}$", "i");
            filters.Add(fb.Regex(s => s.ShiftType, typeRx));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var statusRx = new BsonRegularExpression($"^{Regex.Escape(filter.Status.Trim())}$", "i");
            filters.Add(fb.Regex(s => s.Status, statusRx));
        }

        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim();
            var rx = new BsonRegularExpression(Regex.Escape(kw), "i");
            var matchedEmpIds = await FindEmployeeIdsMatchingKeywordAsync(rx, ct);

            var kwFilters = new List<FilterDefinition<DoctorShift>>
            {
                fb.Regex(s => s.RoomId, rx),
                fb.Regex(s => s.RoomName, rx),
                fb.Regex(s => s.EmployeeName, rx),
                fb.Regex(s => s.Department, rx),
                fb.Regex(s => s.Notes, rx)
            };

            if (matchedEmpIds.Count > 0)
            {
                kwFilters.Add(fb.In(s => s.DoctorId, matchedEmpIds));
            }

            filters.Add(fb.Or(kwFilters));
        }

        var finalFilter = filters.Count > 0 ? fb.And(filters) : FilterDefinition<DoctorShift>.Empty;
        var total = await _shifts.CountDocumentsAsync(finalFilter, cancellationToken: ct);

        var items = await _shifts
            .Find(finalFilter)
            .SortByDescending(s => s.ShiftDate)
            .ThenBy(s => s.ShiftType)
            .ThenBy(s => s.RoomId)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return new PagedResult<DoctorShift>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<DoctorShift>> GetActiveShiftsOnDateAsync(
        DateTime shiftDate,
        string? excludeShiftId = null,
        CancellationToken ct = default)
    {
        var start = DateTime.SpecifyKind(shiftDate.Date, DateTimeKind.Utc);
        var end = start.AddDays(1);
        var fb = Builders<DoctorShift>.Filter;

        var inactiveStatuses = new[] { "Cancelled", "Rejected" };
        var filter = fb.And(
            fb.Gte(s => s.ShiftDate, start),
            fb.Lt(s => s.ShiftDate, end),
            fb.Nin(s => s.Status, inactiveStatuses));

        if (!string.IsNullOrWhiteSpace(excludeShiftId))
        {
            filter = fb.And(filter, fb.Ne(s => s.Id, excludeShiftId.Trim()));
        }

        return await _shifts.Find(filter).ToListAsync(ct);
    }

    public Task InsertAsync(DoctorShift shift, CancellationToken ct = default)
        => _shifts.InsertOneAsync(shift, cancellationToken: ct);

    public async Task<bool> ReplaceAsync(DoctorShift shift, CancellationToken ct = default)
    {
        var result = await _shifts.ReplaceOneAsync(
            Builders<DoctorShift>.Filter.Eq(s => s.Id, shift.Id),
            shift,
            cancellationToken: ct);
        return result.MatchedCount == 1;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var result = await _shifts.DeleteOneAsync(
            Builders<DoctorShift>.Filter.Eq(s => s.Id, id.Trim()),
            cancellationToken: ct);
        return result.DeletedCount == 1;
    }

    public async Task<bool> HasActiveAppointmentsAsync(string shiftId, CancellationToken ct = default)
    {
        try
        {
            var fb = Builders<BsonDocument>.Filter;
            var inactiveStatuses = new[] { "Cancelled", "Rejected" };

            var idMatches = new List<FilterDefinition<BsonDocument>>
            {
                fb.Eq("doctor_shift_id", shiftId)
            };
            if (ObjectId.TryParse(shiftId, out var oid))
            {
                idMatches.Add(fb.Eq("doctor_shift_id", oid));
            }

            var filter = fb.And(
                fb.Or(idMatches),
                fb.Nin("status", inactiveStatuses));

            return await _appointments.Find(filter).Limit(1).AnyAsync(ct);
        }
        catch
        {
            return false;
        }
    }

    public async Task<(Doctor? Doctor, User? Account)> ResolveEmployeeAsync(string employeeId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId))
            return (null, null);

        var trimmed = employeeId.Trim();
        Doctor? doctor = null;
        User? account = null;

        try
        {
            if (ObjectId.TryParse(trimmed, out _))
            {
                doctor = await _doctors.Find(d => d.Id == trimmed).FirstOrDefaultAsync(ct);
            }
        }
        catch
        {
            // Ignore format mismatch
        }

        try
        {
            var uf = Builders<User>.Filter;
            account = await _accounts.Find(uf.Or(
                    uf.Eq(u => u.Id, trimmed),
                    uf.Eq("Username", trimmed),
                    uf.Eq("username", trimmed)))
                .FirstOrDefaultAsync(ct);

            if (doctor == null && account != null && ObjectId.TryParse(account.Id, out _))
            {
                doctor = await _doctors.Find(d => d.Id == account.Id).FirstOrDefaultAsync(ct);
            }
        }
        catch
        {
            // Ignore
        }

        return (doctor, account);
    }

    public async Task<(string? RoomName, string? RoomNumber, string? Floor)> ResolveRoomAsync(string roomId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            return (null, null, null);

        var trimmed = roomId.Trim();
        try
        {
            var docRoom = await _doctorRooms.Find(r => r.Id == trimmed || r.RoomNumber == trimmed).FirstOrDefaultAsync(ct);
            if (docRoom != null)
            {
                return (docRoom.RoomName, docRoom.RoomNumber, docRoom.Floor);
            }

            var labRoom = await _labRooms.Find(Builders<BsonDocument>.Filter.Eq("_id", trimmed)).FirstOrDefaultAsync(ct);
            if (labRoom != null)
            {
                var rName = labRoom.TryGetValue("room_name", out var rn) && rn.IsString ? rn.AsString : trimmed;
                var rType = labRoom.TryGetValue("room_type", out var rt) && rt.IsString ? rt.AsString : null;
                return (rName, trimmed, rType);
            }
        }
        catch
        {
            // Ignore
        }

        return (null, null, null);
    }

    public async Task<IReadOnlyDictionary<string, (string FullName, string Role, string Department, string? LicenseNumber)>> GetEmployeeMetadataAsync(
        IEnumerable<string> employeeIds,
        CancellationToken ct = default)
    {
        var ids = employeeIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var map = new Dictionary<string, (string FullName, string Role, string Department, string? LicenseNumber)>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return map;

        try
        {
            var validOids = ids.Where(id => ObjectId.TryParse(id, out _)).ToList();
            if (validOids.Count > 0)
            {
                var doctors = await _doctors
                    .Find(Builders<Doctor>.Filter.In(d => d.Id, validOids))
                    .ToListAsync(ct);

                foreach (var d in doctors)
                {
                    map[d.Id] = (d.FullName, "Doctor", d.Specialty, d.LicenseNumber);
                }
            }

            var accounts = await _accounts
                .Find(Builders<User>.Filter.In(u => u.Id, ids))
                .ToListAsync(ct);

            foreach (var a in accounts)
            {
                var roleLabel = a.RoleId switch
                {
                    1 => "Admin",
                    2 => "LabHead",
                    3 => "LabTech",
                    4 => "Doctor",
                    5 => "Receptionist",
                    _ => "Staff"
                };

                if (map.TryGetValue(a.Id, out var existing))
                {
                    map[a.Id] = (
                        !string.IsNullOrWhiteSpace(existing.FullName) ? existing.FullName : a.FullName,
                        existing.Role,
                        existing.Department,
                        existing.LicenseNumber);
                }
                else
                {
                    map[a.Id] = (a.FullName, roleLabel, roleLabel == "LabTech" || roleLabel == "LabHead" ? "Laboratory" : "Clinical", null);
                }
            }
        }
        catch
        {
            // Non-fatal
        }

        return map;
    }

    public async Task<IReadOnlyDictionary<string, (string RoomName, string? RoomNumber, string? Floor)>> GetRoomMetadataAsync(
        IEnumerable<string> roomIds,
        CancellationToken ct = default)
    {
        var ids = roomIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        var map = new Dictionary<string, (string RoomName, string? RoomNumber, string? Floor)>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return map;

        try
        {
            var dRooms = await _doctorRooms
                .Find(Builders<DoctorRoom>.Filter.In(r => r.Id, ids))
                .ToListAsync(ct);

            foreach (var r in dRooms)
            {
                map[r.Id] = (r.RoomName, r.RoomNumber, r.Floor);
            }

            var lRooms = await _labRooms
                .Find(Builders<BsonDocument>.Filter.In("_id", ids))
                .ToListAsync(ct);

            foreach (var l in lRooms)
            {
                var id = l.GetValue("_id", BsonNull.Value).ToString() ?? string.Empty;
                var name = l.TryGetValue("room_name", out var rn) && rn.IsString ? rn.AsString : id;
                var floor = l.TryGetValue("room_type", out var rt) && rt.IsString ? rt.AsString : null;
                map[id] = (name, id, floor);
            }
        }
        catch
        {
            // Non-fatal
        }

        return map;
    }

    private async Task<List<string>> FindEmployeeIdsMatchingKeywordAsync(BsonRegularExpression rx, CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var df = Builders<Doctor>.Filter;
            var docs = await _doctors
                .Find(df.Or(df.Regex(d => d.FullName, rx), df.Regex(d => d.Specialty, rx)))
                .Project(d => d.Id)
                .ToListAsync(ct);
            foreach (var id in docs) ids.Add(id);

            var uf = Builders<User>.Filter;
            var users = await _accounts
                .Find(uf.Or(uf.Regex("FullName", rx), uf.Regex("fullName", rx), uf.Regex("Username", rx)))
                .Project(u => u.Id)
                .ToListAsync(ct);
            foreach (var id in users) ids.Add(id);
        }
        catch
        {
            // Ignore
        }
        return ids.ToList();
    }

    public async Task<IReadOnlyList<ShiftRoomDto>> GetAllRoomsAsync(CancellationToken ct = default)
    {
        var result = new List<ShiftRoomDto>();
        try
        {
            var docRooms = await _doctorRooms.Find(r => r.IsActive).ToListAsync(ct);
            foreach (var r in docRooms)
            {
                result.Add(new ShiftRoomDto
                {
                    Id = r.Id,
                    Name = !string.IsNullOrWhiteSpace(r.RoomName) ? r.RoomName : r.Id,
                    RoomNumber = r.RoomNumber,
                    Floor = r.Floor,
                    Department = "Khám Bệnh Lâm Sàng",
                    Type = "Clinic"
                });
            }

            var labRooms = await _labRooms
                .Find(Builders<BsonDocument>.Filter.Ne("is_deleted", true) & Builders<BsonDocument>.Filter.Ne("isActive", false))
                .ToListAsync(ct);
            foreach (var l in labRooms)
            {
                var id = l.GetValue("_id", BsonNull.Value).ToString() ?? string.Empty;
                var name = l.TryGetValue("room_name", out var rn) && rn.IsString ? rn.AsString : id;
                var type = l.TryGetValue("room_type", out var rt) && rt.IsString ? rt.AsString : "Xét nghiệm";
                var floor = l.TryGetValue("location", out var loc) && loc.IsString ? loc.AsString : null;
                result.Add(new ShiftRoomDto
                {
                    Id = id,
                    Name = name,
                    RoomNumber = id,
                    Floor = floor,
                    Department = "Khoa Xét Nghiệm",
                    Type = "Laboratory"
                });
            }
        }
        catch
        {
            // Fallback
        }

        if (result.Count == 0)
        {
            result.AddRange(new[]
            {
                new ShiftRoomDto { Id = "ROOM-101", Name = "Phòng Khám Nội Tổng Quát 101", RoomNumber = "101", Floor = "Tầng 1", Department = "Khám Lâm Sàng", Type = "Clinic" },
                new ShiftRoomDto { Id = "ROOM-102", Name = "Phòng Khám Tim Mạch 102", RoomNumber = "102", Floor = "Tầng 1", Department = "Khám Lâm Sàng", Type = "Clinic" },
                new ShiftRoomDto { Id = "ROOM-103", Name = "Phòng Khám Nội Tiết 103", RoomNumber = "103", Floor = "Tầng 1", Department = "Khám Lâm Sàng", Type = "Clinic" },
                new ShiftRoomDto { Id = "ROOM-201", Name = "Phòng Tư Vấn Kết Quả 201", RoomNumber = "201", Floor = "Tầng 2", Department = "Khám Lâm Sàng", Type = "Clinic" },
                new ShiftRoomDto { Id = "LAB-ROOM-01", Name = "Phòng Xét Nghiệm Huyết Học 01", RoomNumber = "LAB-01", Floor = "Tầng 2", Department = "Khoa Xét Nghiệm", Type = "Laboratory" },
                new ShiftRoomDto { Id = "LAB-ROOM-02", Name = "Phòng Miễn Dịch & Sinh Học Phân Tử 02", RoomNumber = "LAB-02", Floor = "Tầng 2", Department = "Khoa Xét Nghiệm", Type = "Laboratory" },
                new ShiftRoomDto { Id = "SAMPLE-ROOM-01", Name = "Phòng Lấy Mẫu Bệnh Phẩm 01", RoomNumber = "SAMPLE-01", Floor = "Tầng 1", Department = "Tiếp Nhận & Lấy Mẫu", Type = "Phlebotomy" }
            });
        }

        return result;
    }

}