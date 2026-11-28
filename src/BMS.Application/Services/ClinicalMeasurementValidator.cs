using BMS.Core.Entities;
using BMS.Core.Exceptions;

namespace BMS.Application.Services;

/// <summary>
/// Analytical Measurement Range (AMR) &amp; Physiological Plausibility Validator (ISO 15189:2022 / CLSI EP06).
/// Enforces exact per-criterion bounds (negative rejection, zero eligibility, physiological ceiling,
/// qualitative allowed values, and cross-analyte clinical consistency).
/// </summary>
public static class ClinicalMeasurementValidator
{
    public sealed record MeasurementRule(
        double Min,
        double Max,
        bool AllowZero,
        string UnitHint);

    private static readonly Dictionary<string, MeasurementRule> Rules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["RBC"] = new(0.5, 12.0, false, "T/L"),
        ["HGB"] = new(15.0, 260.0, false, "g/L"),
        ["HCT"] = new(5.0, 85.0, false, "%"),
        ["MCV"] = new(40.0, 160.0, false, "fL"),
        ["MCH"] = new(10.0, 60.0, false, "pg"),
        ["MCHC"] = new(200.0, 450.0, false, "g/L"),
        ["RDW_CV"] = new(8.0, 40.0, false, "%"),
        ["WBC"] = new(0.1, 500.0, false, "G/L"),
        ["NEU_PCT"] = new(0.0, 100.0, true, "%"),
        ["LYM_PCT"] = new(0.0, 100.0, true, "%"),
        ["MONO_PCT"] = new(0.0, 100.0, true, "%"),
        ["EOS_PCT"] = new(0.0, 100.0, true, "%"),
        ["BASO_PCT"] = new(0.0, 100.0, true, "%"),
        ["PLT"] = new(1.0, 3000.0, false, "G/L"),

        ["GLU"] = new(0.5, 60.0, false, "mmol/L"),
        ["GLUCOSE"] = new(0.5, 60.0, false, "mmol/L"),
        ["HBA1C"] = new(2.0, 25.0, false, "%"),
        ["AST"] = new(1.0, 15000.0, false, "U/L"),
        ["ALT"] = new(1.0, 15000.0, false, "U/L"),
        ["GGT"] = new(1.0, 5000.0, false, "U/L"),
        ["BILI_T"] = new(0.5, 1000.0, false, "µmol/L"),
        ["BILI_D"] = new(0.0, 600.0, true, "µmol/L"),
        ["ALB"] = new(5.0, 80.0, false, "g/L"),
        ["TP"] = new(15.0, 150.0, false, "g/L"),
        ["UREA"] = new(0.5, 100.0, false, "mmol/L"),
        ["CRE"] = new(10.0, 2500.0, false, "µmol/L"),
        ["CREAT"] = new(10.0, 2500.0, false, "µmol/L"),
        ["CREA"] = new(10.0, 2500.0, false, "µmol/L"),
        ["URIC"] = new(30.0, 1500.0, false, "µmol/L"),
        ["CRP_HS"] = new(0.0, 500.0, true, "mg/L"),
        ["CRP"] = new(0.0, 500.0, true, "mg/L"),

        ["CHOL"] = new(0.5, 30.0, false, "mmol/L"),
        ["CHOL_T"] = new(0.5, 30.0, false, "mmol/L"),
        ["TRIG"] = new(0.1, 60.0, false, "mmol/L"),
        ["HDL_C"] = new(0.1, 6.0, false, "mmol/L"),
        ["LDL_C"] = new(0.1, 20.0, false, "mmol/L"),

        ["NA"] = new(90.0, 200.0, false, "mmol/L"),
        ["K"] = new(1.0, 12.0, false, "mmol/L"),
        ["CL"] = new(60.0, 150.0, false, "mmol/L"),
        ["CA_ION"] = new(0.3, 3.5, false, "mmol/L"),

        ["PT_SEC"] = new(5.0, 150.0, false, "s"),
        ["PT_INR"] = new(0.5, 15.0, false, "INR"),
        ["APTT"] = new(10.0, 200.0, false, "s"),
        ["FIBRIN"] = new(0.3, 15.0, false, "g/L"),

        ["HBS_AB"] = new(0.0, 50000.0, true, "mIU/mL"),
    };

    private static readonly HashSet<string> AllowedQualitativeTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "Âm tính (Negative)",
        "Dương tính (Positive)",
        "Âm tính",
        "Dương tính",
        "Negative",
        "Positive",
        "Non-reactive",
        "Reactive",
        "Không phản ứng",
        "Phản ứng"
    };

    private static readonly string[] WbcDiffCodes = ["NEU_PCT", "LYM_PCT", "MONO_PCT", "EOS_PCT", "BASO_PCT"];

    public static MeasurementRule GetRule(string code, TestCriterionEntity? criterion = null)
    {
        var key = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (Rules.TryGetValue(key, out var rule))
            return rule;

        var unit = (criterion?.Unit ?? string.Empty).Trim();
        if (unit == "%")
            return new(0.0, 100.0, true, "%");

        var allowZero = criterion?.NormalMin is <= 0;
        var min = allowZero ? 0.0 : 0.0001;
        var max = criterion?.NormalMax is > 0 ? Math.Max(criterion.NormalMax.Value * 20.0, 1000.0) : 100000.0;
        return new(min, max, allowZero, unit);
    }

    public static void ValidateNumericValue(TestCriterionEntity criterion, double value)
    {
        var code = criterion.CriterionCode;
        var name = criterion.CriterionName;
        var rule = GetRule(code, criterion);
        var unit = string.IsNullOrWhiteSpace(criterion.Unit) ? rule.UnitHint : criterion.Unit;

        if (!double.IsFinite(value))
            throw new DomainValidationException("INVALID_MEASURED_VALUE", $"Chỉ số {code} ({name}) phải là một số hợp lệ.");

        if (value < 0)
            throw new DomainValidationException("NEGATIVE_MEASURED_VALUE", $"Chỉ số {code} ({name}) không được nhập số âm ({value}).");

        if (!rule.AllowZero && value == 0)
            throw new DomainValidationException("ZERO_NOT_ALLOWED", $"Chỉ số {code} ({name}) không thể bằng 0 về mặt sinh học (dải hợp lệ: {rule.Min} – {rule.Max} {unit}).");

        if (value < rule.Min || value > rule.Max)
            throw new DomainValidationException(
                "OUT_OF_MEASUREMENT_RANGE",
                $"Chỉ số {code} ({name}) = {value} {unit} nằm ngoài dải đo sinh lý cho phép ({rule.Min} – {rule.Max} {unit}). Vui lòng kiểm tra lỗi nhập sai dấu phẩy hoặc đơn vị.");
    }

    public static void ValidateQualitativeValue(TestCriterionEntity criterion, string rawValue)
    {
        var trimmed = (rawValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new DomainValidationException("INVALID_QUALITATIVE_VALUE", $"Chỉ số định tính {criterion.CriterionCode} ({criterion.CriterionName}) bắt buộc chọn Âm tính hoặc Dương tính.");

        if (!AllowedQualitativeTokens.Contains(trimmed))
            throw new DomainValidationException("INVALID_QUALITATIVE_VALUE", $"Chỉ số định tính {criterion.CriterionCode} chỉ chấp nhận giá trị chuẩn (Âm tính / Dương tính).");
    }

    /// <summary>Validates cross-analyte physiological invariants within a single test panel.</summary>
    public static void ValidateCrossAnalyteRules(IReadOnlyList<TestResultItem> items)
    {
        var numericByCode = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (double.TryParse(item.MeasuredValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num))
                numericByCode[item.CriterionCode.Trim().ToUpperInvariant()] = num;
        }

        var wbcPresent = WbcDiffCodes.Where(c => numericByCode.ContainsKey(c)).ToList();
        if (wbcPresent.Count >= 2)
        {
            var sum = wbcPresent.Sum(c => numericByCode[c]);
            if (sum > 100.5)
                throw new DomainValidationException(
                    "INVALID_WBC_DIFFERENTIAL_SUM",
                    $"Tổng tỷ lệ các dòng bạch cầu ({string.Join(" + ", wbcPresent)}) = {sum:0.#}% vượt quá 100%. Vui lòng kiểm tra lại.");
        }

        if (numericByCode.TryGetValue("BILI_D", out var biliD) && numericByCode.TryGetValue("BILI_T", out var biliT) && biliD > biliT)
        {
            throw new DomainValidationException(
                "INVALID_BILIRUBIN_RATIO",
                $"Bilirubin trực tiếp (BILI_D = {biliD}) không thể lớn hơn Bilirubin toàn phần (BILI_T = {biliT}).");
        }

        if (numericByCode.TryGetValue("ALB", out var alb) && numericByCode.TryGetValue("TP", out var tp) && alb > tp)
        {
            throw new DomainValidationException(
                "INVALID_PROTEIN_RATIO",
                $"Albumin (ALB = {alb} g/L) không thể lớn hơn Protein toàn phần (TP = {tp} g/L).");
        }
    }
}
