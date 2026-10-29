namespace BMS.Core.Constants;

/// <summary>A collection tube and its CLSI H3-A6 order-of-draw position (1 = drawn first).</summary>
/// <param name="Color">Tube cap color key used by labels and UI (BlueCulture, LightBlue, Red, Green, Purple, Gray).</param>
/// <param name="TubeType">Additive / tube description.</param>
/// <param name="DrawOrder">Position in the order of draw.</param>
/// <param name="Inversions">Recommended gentle inversions after filling.</param>
public sealed record TubeSpec(string Color, string TubeType, int DrawOrder, string Inversions);

/// <summary>
/// Maps a test to its tube by the CLSI H3-A6 order of draw (BA:L19, TIEU-CHI Bước 3).
/// </summary>
/// <remarks>WHY keyword rules: the catalog (service_packages) has no tube field yet; matching on code/name keeps
/// Glucose → Gray (fluoride) and CBC → Purple (EDTA). The old code mapped NaF → Green, which is wrong.
/// TODO(phat, Đợt 4): move the tube to the catalog so Manager can maintain it.</remarks>
public static class TubeRules
{
    public static readonly TubeSpec BloodCulture = new("BloodCulture", "Chai cấy máu", 1, "8-10");
    public static readonly TubeSpec Citrate = new("LightBlue", "Xanh lam Citrate 3.2% (9:1)", 2, "3-4");
    public static readonly TubeSpec Serum = new("Red", "Đỏ/Vàng SST (huyết thanh)", 3, "5-6");
    public static readonly TubeSpec Heparin = new("Green", "Xanh lá Heparin", 4, "8-10");
    public static readonly TubeSpec Edta = new("Purple", "Tím EDTA", 5, "8-10");
    public static readonly TubeSpec Fluoride = new("Gray", "Xám Fluoride/Oxalate (NaF)", 6, "8-10");

    private static readonly (string[] Keys, TubeSpec Tube)[] Rules =
    [
        (["CULTURE", "CẤY MÁU", "CAY MAU"], BloodCulture),
        (["PT", "APTT", "INR", "FIBRINOGEN", "D-DIMER", "DDIMER", "COAG", "ĐÔNG MÁU"], Citrate),
        (["GLU", "GLUCOSE", "OGTT", "ĐƯỜNG HUYẾT", "DUONG HUYET", "LACTATE"], Fluoride),
        (["CBC", "HBA1C", "ESR", "MÁU LẮNG", "NHÓM MÁU", "ABO", "HUYẾT ĐỒ", "CÔNG THỨC MÁU", "HEMATOLOGY", "HUYẾT HỌC"], Edta),
        (["ABG", "KHÍ MÁU", "ELECTROLYTE", "ĐIỆN GIẢI", "TROPONIN", "AMMONIA"], Heparin),
    ];

    /// <summary>Tube for a test identified by code / name / category; serum (SST) when nothing matches.</summary>
    public static TubeSpec For(string? code, string? name = null, string? category = null)
    {
        var tokens = Tokens(code).Concat(Tokens(name)).Concat(Tokens(category)).ToHashSet(StringComparer.Ordinal);
        var text = $" {code} {name} {category} ".ToUpperInvariant();
        foreach (var (keys, tube) in Rules)
            if (keys.Any(k => Matches(k, tokens, text)))
                return tube;
        return Serum;
    }

    // WHY two modes: short codes (PT, CBC, GLU) must match whole tokens so "PT" does not hit "APTT" or "SEPTIC";
    // long keys and phrases match as substrings ("GLUCOSE" inside "GLUCOSE_FASTING").
    private static bool Matches(string key, HashSet<string> tokens, string text) =>
        key.Length > 4 || key.Contains(' ') || key.Contains('-')
            ? text.Contains(key, StringComparison.Ordinal)
            : tokens.Contains(key);

    private static IEnumerable<string> Tokens(string? s) =>
        (s ?? string.Empty).ToUpperInvariant().Split([' ', '-', '_', '/', ',', '(', ')', '+', '.'], StringSplitOptions.RemoveEmptyEntries);
}
