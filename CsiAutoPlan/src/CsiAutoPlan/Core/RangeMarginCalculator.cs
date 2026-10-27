using System;

namespace CsiAutoPlan.Core
{
    /// <summary>
    /// Proximal and distal field target margins from a field's range (PBSTV margin sheet):
    ///   distal margin   = R x p + a
    ///   proximal margin = (R - SOBP) x p + a
    /// with R = nominal range and SOBP = nominal SOBP width of the field (mm), p = range uncertainty
    /// (3.5 % = 0.035) and a = added margin (3 mm). Example from the sheet: R 10.33 cm, SOBP 7.55 cm ->
    /// distal 0.6616 cm, proximal 0.3973 cm. No ESAPI here, so the formulas are unit-tested.
    /// </summary>
    public static class RangeMarginCalculator
    {
        /// <summary>Ranges outside this window (mm) are treated as "not available" rather than used.</summary>
        public const double MinPlausibleRangeMm = 5, MaxPlausibleRangeMm = 400;

        /// <summary>true if R and SOBP can be used; otherwise <paramref name="why"/> says why not.</summary>
        public static bool IsUsable(double rangeMm, double sobpMm, out string why)
        {
            why = null;
            if (double.IsNaN(rangeMm) || double.IsInfinity(rangeMm) || rangeMm <= 0) why = "no nominal range";
            else if (rangeMm < MinPlausibleRangeMm || rangeMm > MaxPlausibleRangeMm)
                why = string.Format("nominal range {0:F1} mm is outside {1}-{2} mm", rangeMm, MinPlausibleRangeMm, MaxPlausibleRangeMm);
            else if (double.IsNaN(sobpMm) || double.IsInfinity(sobpMm) || sobpMm < 0) why = "no nominal SOBP width";
            else if (sobpMm > rangeMm + 1e-6) why = string.Format("SOBP width {0:F1} mm is larger than the range {1:F1} mm", sobpMm, rangeMm);
            return why == null;
        }

        /// <summary>Distal margin (mm) = R x percent / 100 + addedMm, rounded up to a multiple of roundUpToMm (0 = no rounding).</summary>
        public static double DistalMm(double rangeMm, double percent, double addedMm, double roundUpToMm)
        {
            return RoundUp(rangeMm * percent / 100.0 + addedMm, roundUpToMm);
        }

        /// <summary>Proximal margin (mm) = (R - SOBP) x percent / 100 + addedMm, rounded up to a multiple of roundUpToMm.</summary>
        public static double ProximalMm(double rangeMm, double sobpMm, double percent, double addedMm, double roundUpToMm)
        {
            return RoundUp(Math.Max(0, rangeMm - sobpMm) * percent / 100.0 + addedMm, roundUpToMm);
        }

        /// <summary>Smallest multiple of step that is >= mm (tolerant to floating-point noise); step &lt;= 0 returns mm.</summary>
        public static double RoundUp(double mm, double step)
        {
            if (step <= 0) return mm;
            return Math.Round(Math.Ceiling(mm / step - 1e-6) * step, 6);
        }
    }
}
