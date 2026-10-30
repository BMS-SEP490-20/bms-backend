using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Shifts;
using BMS.Core.Entities;

namespace BMS.Application.Interfaces;

public interface IShiftRepository
{
    Task<DoctorShift?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<PagedResult<DoctorShift>> SearchAsync(ShiftFilterRequest filter, CancellationToken ct = default);
    Task<IReadOnlyList<DoctorShift>> GetActiveShiftsOnDateAsync(DateTime shiftDate, string? excludeShiftId = null, CancellationToken ct = default);
    Task InsertAsync(DoctorShift shift, CancellationToken ct = default);
    Task<bool> ReplaceAsync(DoctorShift shift, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    Task<bool> HasActiveAppointmentsAsync(string shiftId, CancellationToken ct = default);
    Task<(Doctor? Doctor, User? Account)> ResolveEmployeeAsync(string employeeId, CancellationToken ct = default);
    Task<(string? RoomName, string? RoomNumber, string? Floor)> ResolveRoomAsync(string roomId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, (string FullName, string Role, string Department, string? LicenseNumber)>> GetEmployeeMetadataAsync(
        IEnumerable<string> employeeIds,
        CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, (string RoomName, string? RoomNumber, string? Floor)>> GetRoomMetadataAsync(
        IEnumerable<string> roomIds,
        CancellationToken ct = default);
    Task<IReadOnlyList<ShiftRoomDto>> GetAllRoomsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ShiftStaffDto>> GetAllStaffAsync(CancellationToken ct = default);
}

public interface IShiftService
{
    Task<PagedResult<ShiftDto>> ListShiftsAsync(ShiftFilterRequest filter, CancellationToken ct = default);
    Task<ShiftDto> GetShiftByIdAsync(string id, CancellationToken ct = default);
    Task<ShiftDto> CreateShiftAsync(CreateShiftRequest request, string? userId, CancellationToken ct = default);
    Task<ShiftDto> UpdateShiftAsync(string id, UpdateShiftRequest request, string? userId, CancellationToken ct = default);
    Task DeleteShiftAsync(string id, string? userId, CancellationToken ct = default);
    Task<IReadOnlyList<ShiftRoomDto>> GetRoomsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ShiftStaffDto>> GetStaffAsync(CancellationToken ct = default);
}
