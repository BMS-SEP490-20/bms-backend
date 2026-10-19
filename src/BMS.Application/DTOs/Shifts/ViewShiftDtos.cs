namespace BMS.ViewShifts.DTOs;

/// <summary>
/// UC36 is read-only and uses official DTOs:
///   BMS.Application.DTOs.Shifts.ShiftDto
///   BMS.Application.DTOs.Shifts.ShiftFilterRequest
///   BMS.Application.DTOs.Common.PagedResult&lt;T&gt;
///
/// Prefer production endpoints:
///   GET /api/shifts
///   GET /api/shifts/{id}
/// Optional branch routes:
///   GET /api/view-shifts
///   GET /api/view-shifts/day?date=
/// </summary>
public static class ViewShiftDtoMarkers
{
}
