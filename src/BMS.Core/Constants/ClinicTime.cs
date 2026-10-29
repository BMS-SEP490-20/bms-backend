namespace BMS.Core.Constants;

/// <summary>
/// Clinic-local calendar helpers. Data is stored in UTC; business days (queue reset at 00:00, PAT-/SMP- daily
/// sequences) follow Vietnam time.
/// </summary>
/// <remarks>WHY a fixed +07:00 offset: Vietnam has no daylight saving time, and a fixed offset does not depend
/// on the host having tzdata (Linux containers often ship without it).</remarks>
public static class ClinicTime
{
    /// <summary>Vietnam offset from UTC.</summary>
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    /// <summary>Clinic-local calendar date of a UTC instant.</summary>
    public static DateOnly LocalDate(DateTime utc) =>
        DateOnly.FromDateTime(DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(Offset));

    /// <summary>UTC instant at which the given clinic-local date starts (00:00 +07:00).</summary>
    public static DateTime StartOfLocalDayUtc(DateOnly localDate) =>
        DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue) - Offset, DateTimeKind.Utc);

    /// <summary>ISO key (yyyy-MM-dd) of the clinic-local date, used for per-day counters and indexes.</summary>
    public static string LocalDateKey(DateTime utc) => LocalDate(utc).ToString("yyyy-MM-dd");
}
