using System;

namespace BMS.Core;

/// <summary>
/// Clinic calendar in Vietnam (UTC+7, no daylight saving).
/// Appointment dates are stored as UTC instants, so a local midnight can land on the previous UTC day.
/// Shift dates are stored as UTC midnight of the calendar day. Both must be compared on the clinic day.
/// </summary>
public static class ClinicClock
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    public static DateTime CalendarDate(DateTime value)
    {
        if (value.Kind == DateTimeKind.Unspecified)
            return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
        return DateTime.SpecifyKind(local.Date, DateTimeKind.Utc);
    }

    /// <summary>
    /// Inclusive start and exclusive end that cover both a UTC midnight calendar day
    /// and the same local midnight stored seven hours earlier.
    /// </summary>
    public static (DateTime Start, DateTime End) DayWindow(DateTime value)
    {
        var day = CalendarDate(value);
        var start = DateTime.SpecifyKind(day.AddHours(-7), DateTimeKind.Utc);
        return (start, start.AddDays(1));
    }

    private static TimeZoneInfo ResolveZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
    }
}
