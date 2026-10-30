namespace BMS.Core.Utilities;

/// <summary>
/// Masks personal data before it reaches logs or public screens (INV-4, BA principle 7).
/// </summary>
public static class PiiMasker
{
    /// <summary>
    /// Keeps the first 3 and last 3 characters of an identifier (CCCD, BHYT card, phone) and stars the rest,
    /// e.g. <c>DN4012345678901</c> → <c>DN4*********901</c>. Short values are fully masked.
    /// </summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var v = value.Trim();
        if (v.Length <= 6) return new string('*', v.Length);
        return string.Concat(v.AsSpan(0, 3), new string('*', v.Length - 6), v.AsSpan(v.Length - 3));
    }

    /// <summary>
    /// Masks a person's name to initials plus the given name, e.g. <c>Nguyễn Văn An</c> → <c>N. V. An</c>,
    /// so queue boards and logs do not expose full names.
    /// </summary>
    public static string MaskName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][..1] + ".";
        return string.Join(' ', parts[..^1].Select(p => p[..1] + ".")) + " " + parts[^1];
    }
}
