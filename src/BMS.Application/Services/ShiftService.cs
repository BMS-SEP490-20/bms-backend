using BMS.Core.Constants;
using System.Globalization;
using BMS.Application.DTOs.Common;
using BMS.Application.DTOs.Shifts;
using BMS.Application.Interfaces;
using BMS.Core.Entities;
using BMS.Core.Exceptions;
using BMS.Core.Interfaces;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace BMS.Application.Services;

/// <summary>
/// Application service implementing View Shifts, Create Shifts, Update Shifts, and Delete Shifts use cases (UC35).
/// Enforces working time validation, employee/room assignment conflict detection, dependency checks, and audit trails.
/// </summary>
public sealed class ShiftService(
    IShiftRepository shiftRepository,
    TimeProvider clock,
    ILogger<ShiftService> logger,
    IAuditLogRepository? auditLogs = null) : IShiftService
{
    public async Task<PagedResult<ShiftDto>> ListShiftsAsync(ShiftFilterRequest filter, CancellationToken ct = default)
    {
        var paged = await shiftRepository.SearchAsync(filter, ct);
        var empMap = await shiftRepository.GetEmployeeMetadataAsync(paged.Items.Select(s => s.DoctorId), ct);
        var roomMap = await shiftRepository.GetRoomMetadataAsync(paged.Items.Select(s => s.RoomId), ct);

        var dtos = paged.Items.Select(s => MapToDto(s, empMap, roomMap)).ToList();
        return new PagedResult<ShiftDto>(dtos, paged.Page, paged.PageSize, paged.TotalItems);
    }

    public Task<ShiftDto> GetShiftByIdAsync(string id, CancellationToken ct = default)
        => GetShiftByIdAsync(id, requesterUserId: null, isManagerOrAdmin: true, ct);

    public async Task<ShiftDto> GetShiftByIdAsync(
        string id,
        string? requesterUserId,
        bool isManagerOrAdmin,
        CancellationToken ct = default)
    {
        var shift = await shiftRepository.GetByIdAsync(id, ct)
                    ?? throw new ShiftNotFoundException(id);

        if (!isManagerOrAdmin)
        {
            var isOwner = !string.IsNullOrWhiteSpace(requesterUserId) &&
                          string.Equals(shift.DoctorId, requesterUserId, StringComparison.OrdinalIgnoreCase);

            if (!isOwner)
            {
                if (auditLogs != null)
                {
                    try
                    {
                        var now = clock.GetUtcNow().UtcDateTime;
                        await auditLogs.AddAsync(new AuditLog
                        {
                            AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                            Timestamp = now,
                            UserId = requesterUserId ?? "ANONYMOUS",
                            UserName = requesterUserId ?? "ANONYMOUS",
                            UserRole = "Staff",
                            Action = "VIEW_SHIFT_DETAILS",
                            Category = "SHIFT_MANAGEMENT",
                            Severity = "WARNING",
                            Status = "ACCESS_DENIED",
                            IpAddress = "127.0.0.1",
                            TargetEntity = "DoctorShift",
                            EntityId = shift.Id,
                            Details = $"Unauthorized access attempt to shift {shift.Id} of employee {shift.DoctorId} by user {requesterUserId}."
                        }, ct);
                    }
                    catch { /* Skip audit failure */ }
                }

                throw new ForbiddenAccessException("Bạn không có quyền xem thông tin chi tiết ca làm việc của nhân viên khác.");
            }
        }

        var empMap = await shiftRepository.GetEmployeeMetadataAsync([shift.DoctorId], ct);
        var roomMap = await shiftRepository.GetRoomMetadataAsync([shift.RoomId], ct);

        return MapToDto(shift, empMap, roomMap);
    }

    public async Task<ShiftDto> CreateShiftAsync(CreateShiftRequest request, string? userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var shiftDate = DateTime.SpecifyKind((request.ShiftDate ?? now).Date, DateTimeKind.Utc);
        
        // Validation MSG-ERR-SHIFT-01: Chặn phân ca quá khứ
        if (shiftDate < now.Date)
        {
            throw new DomainValidationException(
                "MSG-ERR-SHIFT-01",
                "Không thể phân ca làm việc trong quá khứ. Vui lòng chọn ngày từ hôm nay trở đi.");
        }

        var (startStr, endStr) = ShiftWriteRequest.ResolveWorkingTimeWindow(request.ShiftType, request.ShiftStart, request.ShiftEnd);
        var (startTs, endTs) = ParseWorkingHoursOrThrow(startStr, endStr);

        // 1. Resolve Employee / Doctor assignment
        var rawEmpId = request.EffectiveEmployeeId;
        var (doctor, account) = await shiftRepository.ResolveEmployeeAsync(rawEmpId, ct);
        if (doctor == null && account == null)
        {
            throw new DomainValidationException(
                "STAFF_NOT_FOUND",
                $"Nhân sự '{rawEmpId}' không tồn tại trong hệ thống.");
        }

        if (account != null && (!account.IsActive || account.Status != (int)UserStatus.Active))
        {
            throw new DomainValidationException(
                "STAFF_INACTIVE",
                $"Tài khoản nhân sự '{rawEmpId}' đang ở trạng thái ngừng hoạt động hoặc bị khóa.");
        }

        var resolvedEmployeeId = account?.Id ?? doctor?.Id ?? rawEmpId;
        var resolvedEmployeeName = !string.IsNullOrWhiteSpace(request.EmployeeName)
            ? request.EmployeeName.Trim()
            : doctor?.FullName ?? account?.FullName ?? rawEmpId;
        var resolvedEmployeeRole = !string.IsNullOrWhiteSpace(request.EmployeeRole)
            ? request.EmployeeRole.Trim()
            : (doctor != null ? "Doctor" : ResolveAccountRoleName(account));
        var resolvedDepartment = !string.IsNullOrWhiteSpace(request.Department)
            ? request.Department.Trim()
            : doctor?.Specialty ?? (resolvedEmployeeRole.StartsWith("Lab", StringComparison.OrdinalIgnoreCase) ? "Laboratory" : "Clinical");

        // 2. Resolve Room assignment
        var roomId = request.RoomId.Trim().ToUpperInvariant();
        var (roomName, _, _, roomSpecialty) = await shiftRepository.ResolveRoomAsync(roomId, ct);

        // Validation MSG-ERR-SHIFT-04: Khớp Tam giác chuyên ngành (Bác sĩ chuyên ngành == Phòng khám chuyên ngành)
        if (string.Equals(resolvedEmployeeRole, "Doctor", StringComparison.OrdinalIgnoreCase) || doctor != null)
        {
            var docSpec = !string.IsNullOrWhiteSpace(doctor?.Specialty) ? doctor.Specialty.Trim() : resolvedDepartment;
            if (!string.IsNullOrWhiteSpace(roomSpecialty) &&
                !string.IsNullOrWhiteSpace(docSpec) &&
                !roomSpecialty.Equals("Tổng quát", StringComparison.OrdinalIgnoreCase) &&
                !docSpec.Equals("Tổng quát", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(roomSpecialty, docSpec, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainValidationException(
                    "MSG-ERR-SHIFT-04",
                    $"Chuyên ngành Bác sĩ ({docSpec}) không khớp với chuyên ngành Phòng khám ({roomSpecialty}). Vui lòng chọn phòng chuyên khoa phù hợp hoặc phòng Tổng quát.");
            }
        }

        // 3. Conflict Detection (Employee overlap & Room overlap on same calendar date)
        await ValidateNoShiftConflictAsync(
            shiftDate,
            startTs,
            endTs,
            startStr,
            endStr,
            resolvedEmployeeId,
            resolvedEmployeeName,
            roomId,
            excludeShiftId: null,
            ct);

        var entity = new DoctorShift
        {
            Id = ObjectId.GenerateNewId().ToString(),
            DoctorId = resolvedEmployeeId,
            EmployeeName = resolvedEmployeeName,
            EmployeeRole = resolvedEmployeeRole,
            Department = resolvedDepartment,
            RoomId = roomId,
            RoomName = roomName ?? roomId,
            ShiftDate = shiftDate,
            ShiftType = NormalizeShiftType(request.ShiftType),
            ShiftStart = startStr,
            ShiftEnd = endStr,
            MaxPatients = request.MaxPatients,
            CurrentPatientsCount = 0,
            Status = NormalizeStatus(request.Status),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedAt = now,
            CreatedBy = userId
        };

        await shiftRepository.InsertAsync(entity, ct);
        logger.LogInformation(
            "Shift created: {ShiftId} on {Date:yyyy-MM-dd} ({Start}-{End}) for {Employee} in {Room} by {UserId}",
            entity.Id, entity.ShiftDate, entity.ShiftStart, entity.ShiftEnd, entity.EmployeeName, entity.RoomId, userId);

        if (auditLogs != null)
        {
            try
            {
                await auditLogs.AddAsync(new AuditLog
                {
                    AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = now,
                    UserId = userId ?? "SYSTEM",
                    UserName = userId ?? "SYSTEM",
                    UserRole = "Manager",
                    Action = "CREATE_SHIFT",
                    Category = "SHIFT_MANAGEMENT",
                    Severity = "INFO",
                    Status = "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "DoctorShift",
                    EntityId = entity.Id,
                    Details = $"Created shift {entity.Id} on {entity.ShiftDate:yyyy-MM-dd} ({entity.ShiftStart}-{entity.ShiftEnd}) for {entity.EmployeeName} in room {entity.RoomId}."
                }, ct);
            }
            catch { /* Skip audit failure */ }
        }

        var empMap = await shiftRepository.GetEmployeeMetadataAsync([entity.DoctorId], ct);
        var roomMap = await shiftRepository.GetRoomMetadataAsync([entity.RoomId], ct);
        return MapToDto(entity, empMap, roomMap);
    }

    public async Task<ShiftDto> UpdateShiftAsync(
        string id,
        UpdateShiftRequest request,
        string? userId,
        CancellationToken ct = default)
    {
        var existing = await shiftRepository.GetByIdAsync(id, ct)
                       ?? throw new ShiftNotFoundException(id);

        // Validation: Không cho sửa ca đã khóa/chốt
        if (string.Equals(existing.Status, "Completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(existing.Status, "Locked", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException(
                "SHIFT_LOCKED",
                $"Ca làm việc '{existing.Id}' đã ở trạng thái {existing.Status} (đã chốt/khóa) và không thể chỉnh sửa.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var shiftDate = DateTime.SpecifyKind((request.ShiftDate ?? existing.ShiftDate).Date, DateTimeKind.Utc);

        // Validation MSG-ERR-SHIFT-01: Chặn phân ca quá khứ
        if (shiftDate < now.Date)
        {
            throw new DomainValidationException(
                "MSG-ERR-SHIFT-01",
                "Không thể phân ca làm việc trong quá khứ. Vui lòng chọn ngày từ hôm nay trở đi.");
        }

        var (startStr, endStr) = ShiftWriteRequest.ResolveWorkingTimeWindow(request.ShiftType, request.ShiftStart, request.ShiftEnd);
        var (startTs, endTs) = ParseWorkingHoursOrThrow(startStr, endStr);

        // Validate capacity against existing booked patients
        if (request.MaxPatients < existing.CurrentPatientsCount)
        {
            throw new DomainValidationException(
                "INVALID_SHIFT_CAPACITY",
                $"Số lượng bệnh nhân tối đa ({request.MaxPatients}) không được nhỏ hơn số lượng bệnh nhân đã đặt lịch hiện tại ({existing.CurrentPatientsCount}).");
        }

        // Resolve Employee / Doctor assignment
        var rawEmpId = request.EffectiveEmployeeId;
        var (doctor, account) = await shiftRepository.ResolveEmployeeAsync(rawEmpId, ct);
        if (doctor == null && account == null)
        {
            throw new DomainValidationException(
                "STAFF_NOT_FOUND",
                $"Nhân sự '{rawEmpId}' không tồn tại trong hệ thống.");
        }

        if (account != null && (!account.IsActive || account.Status != (int)UserStatus.Active))
        {
            throw new DomainValidationException(
                "STAFF_INACTIVE",
                $"Tài khoản nhân sự '{rawEmpId}' đang ở trạng thái ngừng hoạt động hoặc bị khóa.");
        }

        var resolvedEmployeeId = account?.Id ?? doctor?.Id ?? rawEmpId;
        var resolvedEmployeeName = !string.IsNullOrWhiteSpace(request.EmployeeName)
            ? request.EmployeeName.Trim()
            : doctor?.FullName ?? account?.FullName ?? existing.EmployeeName ?? rawEmpId;
        var resolvedEmployeeRole = !string.IsNullOrWhiteSpace(request.EmployeeRole)
            ? request.EmployeeRole.Trim()
            : (doctor != null ? "Doctor" : existing.EmployeeRole ?? ResolveAccountRoleName(account));
        var resolvedDepartment = !string.IsNullOrWhiteSpace(request.Department)
            ? request.Department.Trim()
            : doctor?.Specialty ?? existing.Department ?? "Clinical";

        var roomId = request.RoomId.Trim().ToUpperInvariant();
        var (roomName, _, _, roomSpecialty) = await shiftRepository.ResolveRoomAsync(roomId, ct);

        // Validation MSG-ERR-SHIFT-04: Khớp Tam giác chuyên ngành (Bác sĩ chuyên ngành == Phòng khám chuyên ngành)
        if (string.Equals(resolvedEmployeeRole, "Doctor", StringComparison.OrdinalIgnoreCase) || doctor != null)
        {
            var docSpec = !string.IsNullOrWhiteSpace(doctor?.Specialty) ? doctor.Specialty.Trim() : resolvedDepartment;
            if (!string.IsNullOrWhiteSpace(roomSpecialty) &&
                !string.IsNullOrWhiteSpace(docSpec) &&
                !roomSpecialty.Equals("Tổng quát", StringComparison.OrdinalIgnoreCase) &&
                !docSpec.Equals("Tổng quát", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(roomSpecialty, docSpec, StringComparison.OrdinalIgnoreCase))
            {
                throw new DomainValidationException(
                    "MSG-ERR-SHIFT-04",
                    $"Chuyên ngành Bác sĩ ({docSpec}) không khớp với chuyên ngành Phòng khám ({roomSpecialty}). Vui lòng chọn phòng chuyên khoa phù hợp hoặc phòng Tổng quát.");
            }
        }

        // Conflict Detection (excluding current shift ID)
        var newStatus = NormalizeStatus(request.Status);
        if (!newStatus.Equals("Cancelled", StringComparison.OrdinalIgnoreCase) &&
            !newStatus.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
        {
            await ValidateNoShiftConflictAsync(
                shiftDate,
                startTs,
                endTs,
                startStr,
                endStr,
                resolvedEmployeeId,
                resolvedEmployeeName,
                roomId,
                excludeShiftId: existing.Id,
                ct);
        }

        var oldDate = existing.ShiftDate;
        var oldStart = existing.ShiftStart;
        var oldEnd = existing.ShiftEnd;
        var oldRoom = existing.RoomId;
        var oldStatus = existing.Status;

        existing.DoctorId = resolvedEmployeeId;
        existing.EmployeeName = resolvedEmployeeName;
        existing.EmployeeRole = resolvedEmployeeRole;
        existing.Department = resolvedDepartment;
        existing.RoomId = roomId;
        existing.RoomName = roomName ?? existing.RoomName ?? roomId;
        existing.ShiftDate = shiftDate;
        existing.ShiftType = NormalizeShiftType(request.ShiftType);
        existing.ShiftStart = startStr;
        existing.ShiftEnd = endStr;
        existing.MaxPatients = request.MaxPatients;
        existing.Status = newStatus;
        existing.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        existing.UpdatedAt = now;
        existing.UpdatedBy = userId;

        if (!await shiftRepository.ReplaceAsync(existing, ct))
            throw new ShiftNotFoundException(id);

        logger.LogInformation("Shift updated: {ShiftId} by {UserId}", existing.Id, userId);

        if (auditLogs != null)
        {
            try
            {
                await auditLogs.AddAsync(new AuditLog
                {
                    AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = now,
                    UserId = userId ?? "SYSTEM",
                    UserName = userId ?? "SYSTEM",
                    UserRole = "Manager",
                    Action = "UPDATE_SHIFT",
                    Category = "SHIFT_MANAGEMENT",
                    Severity = "INFO",
                    Status = "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "DoctorShift",
                    EntityId = existing.Id,
                    Details = $"Updated shift {existing.Id}: Time [{oldStart}-{oldEnd}]->[{startStr}-{endStr}], Room [{oldRoom}]->[{roomId}], Date [{oldDate:yyyy-MM-dd}]->[{shiftDate:yyyy-MM-dd}], Status [{oldStatus}]->[{newStatus}]."
                }, ct);
            }
            catch { /* Skip audit failure */ }
        }

        var empMap = await shiftRepository.GetEmployeeMetadataAsync([existing.DoctorId], ct);
        var roomMap = await shiftRepository.GetRoomMetadataAsync([existing.RoomId], ct);
        return MapToDto(existing, empMap, roomMap);
    }

    public async Task DeleteShiftAsync(string id, string? userId, CancellationToken ct = default)
    {
        var existing = await shiftRepository.GetByIdAsync(id, ct)
                       ?? throw new ShiftNotFoundException(id);

        var now = clock.GetUtcNow().UtcDateTime;

        // Check if shift has started or is in the past
        var isPastOrStarted = existing.ShiftDate.Date < now.Date;
        if (existing.ShiftDate.Date == now.Date)
        {
            var (startStr, _) = ShiftWriteRequest.ResolveWorkingTimeWindow(existing.ShiftType, existing.ShiftStart, existing.ShiftEnd);
            if (TimeSpan.TryParseExact(startStr, @"hh\:mm", CultureInfo.InvariantCulture, out var startTs))
            {
                isPastOrStarted = now.TimeOfDay >= startTs;
            }
        }

        // Check if shift has activity (registered patients or linked appointments)
        var hasActivity = existing.CurrentPatientsCount > 0 || await shiftRepository.HasActiveAppointmentsAsync(existing.Id, ct);

        bool softCancelled = false;
        if (isPastOrStarted || hasActivity)
        {
            // UC35.5 (SHIFT-DEL-02 & SHIFT-DEL-03): Ca đã bắt đầu hoặc đã có dữ liệu hoạt động -> chuyển sang Cancelled thay vì xóa cứng
            existing.Status = "Cancelled";
            existing.UpdatedAt = now;
            existing.UpdatedBy = userId;
            existing.Notes = string.IsNullOrWhiteSpace(existing.Notes)
                ? "[Đã hủy ca làm việc do yêu cầu xóa ca]"
                : $"{existing.Notes} [Đã hủy ca làm việc do yêu cầu xóa ca]";
            await shiftRepository.ReplaceAsync(existing, ct);
            softCancelled = true;
        }
        else
        {
            // UC35.5 (SHIFT-DEL-01): Ca chưa bắt đầu và chưa có dữ liệu liên quan -> xóa cứng
            if (!await shiftRepository.DeleteAsync(existing.Id, ct))
                throw new ShiftNotFoundException(id);
        }

        if (auditLogs != null)
        {
            try
            {
                await auditLogs.AddAsync(new AuditLog
                {
                    AuditId = $"AUD-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                    Timestamp = now,
                    UserId = userId ?? "SYSTEM",
                    UserName = userId ?? "SYSTEM",
                    UserRole = "Manager",
                    Action = "DELETE_SHIFT",
                    Category = "SHIFT_MANAGEMENT",
                    Severity = "INFO",
                    Status = "SUCCESS",
                    IpAddress = "127.0.0.1",
                    TargetEntity = "DoctorShift",
                    EntityId = existing.Id,
                    Details = $"Shift {existing.Id} on {existing.ShiftDate:yyyy-MM-dd} for {existing.EmployeeName} was {(softCancelled ? "marked as Cancelled (soft-delete)" : "permanently deleted (hard-delete)")} by user {userId}."
                }, ct);
            }
            catch { /* Skip audit failure */ }
        }

        logger.LogInformation(
            "Shift {ShiftId} {Action} by {UserId}",
            existing.Id, softCancelled ? "soft-cancelled" : "deleted", userId);
    }

    public async Task<IReadOnlyList<ShiftRoomDto>> GetRoomsAsync(CancellationToken ct = default)
    {
        return await shiftRepository.GetAllRoomsAsync(ct);
    }

    public async Task<IReadOnlyList<ShiftStaffDto>> GetStaffAsync(CancellationToken ct = default)
    {
        return await shiftRepository.GetAllStaffAsync(ct);
    }

    private async Task ValidateNoShiftConflictAsync(
        DateTime shiftDate,
        TimeSpan newStart,
        TimeSpan newEnd,
        string newStartStr,
        string newEndStr,
        string employeeId,
        string employeeName,
        string roomId,
        string? excludeShiftId,
        CancellationToken ct)
    {
        var activeShiftsOnDate = await shiftRepository.GetActiveShiftsOnDateAsync(shiftDate, excludeShiftId, ct);
        foreach (var s in activeShiftsOnDate)
        {
            var (existStartStr, existEndStr) = ShiftWriteRequest.ResolveWorkingTimeWindow(s.ShiftType, s.ShiftStart, s.ShiftEnd);
            if (!TimeSpan.TryParseExact(existStartStr, @"hh\:mm", CultureInfo.InvariantCulture, out var existStart) ||
                !TimeSpan.TryParseExact(existEndStr, @"hh\:mm", CultureInfo.InvariantCulture, out var existEnd))
            {
                continue;
            }

            // Two intervals [newStart, newEnd) and [existStart, existEnd) overlap iff newStart < existEnd && existStart < newEnd
            var overlaps = newStart < existEnd && existStart < newEnd;
            if (!overlaps)
                continue;

            if (string.Equals(s.DoctorId, employeeId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ShiftConflictException(
                    "SHIFT_EMPLOYEE_CONFLICT",
                    $"Nhân sự '{employeeName}' ({employeeId}) đã được phân công ca '{s.ShiftType}' ({existStartStr} - {existEndStr}) tại phòng '{s.RoomId}' vào ngày {shiftDate:yyyy-MM-dd}, bị trùng với khung giờ ({newStartStr} - {newEndStr}).");
            }

            if (!string.IsNullOrWhiteSpace(roomId) &&
                string.Equals(s.RoomId, roomId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ShiftConflictException(
                    "SHIFT_ROOM_CONFLICT",
                    $"Phòng '{roomId}' đã được phân công cho ca '{s.ShiftType}' ({existStartStr} - {existEndStr}) vào ngày {shiftDate:yyyy-MM-dd}, bị trùng với khung giờ ({newStartStr} - {newEndStr}).");
            }
        }
    }

    private static (TimeSpan Start, TimeSpan End) ParseWorkingHoursOrThrow(string startStr, string endStr)
    {
        if (!TimeSpan.TryParseExact(startStr, @"hh\:mm", CultureInfo.InvariantCulture, out var startTs) ||
            !TimeSpan.TryParseExact(endStr, @"hh\:mm", CultureInfo.InvariantCulture, out var endTs))
        {
            throw new DomainValidationException(
                "INVALID_WORKING_TIME",
                "Giờ bắt đầu và giờ kết thúc ca làm việc phải theo định dạng HH:mm (ví dụ: 07:00 - 11:30).");
        }

        if (startTs >= endTs)
        {
            throw new DomainValidationException(
                "INVALID_WORKING_TIME",
                $"Giờ bắt đầu ({startStr}) phải nhỏ hơn giờ kết thúc ({endStr}).");
        }

        return (startTs, endTs);
    }

    private static string NormalizeShiftType(string shiftType) => shiftType.Trim().ToLowerInvariant() switch
    {
        "morning" => "Morning",
        "afternoon" => "Afternoon",
        "evening" => "Evening",
        "night" => "Night",
        "fullday" => "FullDay",
        _ => shiftType.Trim()
    };

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return "Approved";
        return status.Trim().ToLowerInvariant() switch
        {
            "approved" => "Approved",
            "scheduled" => "Scheduled",
            "active" => "Active",
            "completed" => "Completed",
            "cancelled" => "Cancelled",
            "pending" => "Pending",
            "rejected" => "Rejected",
            _ => status.Trim()
        };
    }

    // 6-role seed: staff roles 1–5 use canonical names; anything else is shown as generic staff.
    private static string ResolveAccountRoleName(User? account)
        => account?.RoleId is >= 1 and <= 5 ? RoleNames.FromRoleId(account.RoleId)! : "Staff";

    private static ShiftDto MapToDto(
        DoctorShift s,
        IReadOnlyDictionary<string, (string FullName, string Role, string Department, string? LicenseNumber)> empMap,
        IReadOnlyDictionary<string, (string RoomName, string? RoomNumber, string? Floor)> roomMap)
    {
        empMap.TryGetValue(s.DoctorId, out var emp);
        roomMap.TryGetValue(s.RoomId, out var room);

        var (startStr, endStr) = ShiftWriteRequest.ResolveWorkingTimeWindow(s.ShiftType, s.ShiftStart, s.ShiftEnd);
        int durationMinutes = 0;
        if (TimeSpan.TryParseExact(startStr, @"hh\:mm", CultureInfo.InvariantCulture, out var st) &&
            TimeSpan.TryParseExact(endStr, @"hh\:mm", CultureInfo.InvariantCulture, out var et) &&
            et > st)
        {
            durationMinutes = (int)(et - st).TotalMinutes;
        }

        return new ShiftDto
        {
            Id = s.Id,
            EmployeeId = s.DoctorId,
            DoctorId = s.DoctorId,
            EmployeeName = !string.IsNullOrWhiteSpace(s.EmployeeName) ? s.EmployeeName : emp.FullName ?? s.DoctorId,
            EmployeeRole = !string.IsNullOrWhiteSpace(s.EmployeeRole) ? s.EmployeeRole : emp.Role ?? "Doctor",
            Department = !string.IsNullOrWhiteSpace(s.Department) ? s.Department : emp.Department ?? "Clinical",
            LicenseNumber = emp.LicenseNumber,
            RoomId = s.RoomId,
            RoomName = !string.IsNullOrWhiteSpace(s.RoomName) ? s.RoomName : room.RoomName ?? s.RoomId,
            RoomNumber = room.RoomNumber ?? s.RoomId,
            Floor = room.Floor,
            ShiftDate = s.ShiftDate,
            ShiftType = s.ShiftType,
            ShiftStart = startStr,
            ShiftEnd = endStr,
            WorkingHoursLabel = $"{startStr} - {endStr}",
            DurationMinutes = durationMinutes,
            MaxPatients = s.MaxPatients,
            CurrentPatientsCount = s.CurrentPatientsCount,
            Status = s.Status,
            Notes = s.Notes,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
            CreatedBy = s.CreatedBy,
            UpdatedBy = s.UpdatedBy
        };
    }
}
