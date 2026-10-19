using BMS.Core.Entities;

namespace BMS.TestResults.Services;

/// <summary>BR-LAB-02 reference flags + BR-LAB-03 panic thresholds.</summary>
public static class PanicEvaluator
{
    public static ResultFlag Evaluate(string code, double value, double? refLow, double? refHigh)
    {
        var c = code.ToUpperInvariant();

        if (c is "GLU" or "GLUCOSE")
        {
            if (value < 40 || value > 450) return ResultFlag.CriticalPanic;
        }
        if (c is "K" or "POTASSIUM")
        {
            if (value < 2.5 || value > 6.5) return ResultFlag.CriticalPanic;
        }
        if (c is "NA" or "SODIUM")
        {
            if (value < 120 || value > 160) return ResultFlag.CriticalPanic;
        }
        if (c is "PLT" or "PLATELET")
        {
            if (value < 20 || value > 1000) return ResultFlag.CriticalPanic;
        }
        if (c is "HB" or "HGB" or "HEMOGLOBIN")
        {
            if (value < 7.0) return ResultFlag.CriticalPanic;
        }

        if (refLow.HasValue && value < refLow.Value) return ResultFlag.Low;
        if (refHigh.HasValue && value > refHigh.Value) return ResultFlag.High;
        return ResultFlag.Normal;
    }
}
