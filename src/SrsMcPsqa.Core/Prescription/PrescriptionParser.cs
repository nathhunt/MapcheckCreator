using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SrsMcPsqa.Core.Prescription
{
    /// <summary>Where a PTV's prescription came from.</summary>
    public enum RxSource
    {
        None,
        /// <summary>Parsed from the PTV ID.</summary>
        FromName,
        /// <summary>Matching RT prescription target.</summary>
        FromPrescription,
        /// <summary>Plan prescription total dose.</summary>
        PlanRx,
        /// <summary>Entered by the user.</summary>
        User,
    }

    public static class RxSourceText
    {
        public static string Describe(this RxSource s) => s switch
        {
            RxSource.FromName => "from name",
            RxSource.FromPrescription => "from prescription",
            RxSource.PlanRx => "plan Rx",
            RxSource.User => "user",
            _ => "none",
        };
    }

    /// <summary>
    /// Guesses a PTV's total prescription dose from its structure ID. Rules, case-insensitive, first match wins:
    /// <list type="number">
    /// <item><c>N x D Gy</c> (e.g. <c>3x9Gy</c>) → N·D Gy. (Added so per-fraction notation isn't read as the total.)</item>
    /// <item><c>D Gy</c> (decimal point or comma allowed, spaces/underscores before "Gy") → D Gy.</item>
    /// <item><c>D cGy</c> → D/100 Gy.</item>
    /// <item>A bare whole number ≥ 100 (e.g. <c>PTV_2400</c>) → cGy.</item>
    /// <item>A number &lt; 100 followed by a Gy-like "G" (e.g. <c>PTV_24G</c>) → Gy.</item>
    /// </list>
    /// Fraction counts (<c>x3</c>, <c>3#</c>, <c>3fx</c>, <c>3F</c>, <c>/3</c>) are removed before rules 4–5. Results outside 1–100 Gy are rejected.
    /// </summary>
    public static class PrescriptionParser
    {
        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private const string Num = @"(\d+(?:[.,]\d+)?)";

        private static readonly Regex FractionsTimesDose = new Regex(@"(?<![\d.,])(\d{1,2})\s*[x×]\s*" + Num + @"[\s_]*Gy(?![a-z])", Opt);
        private static readonly Regex GyRule = new Regex(@"(?<![\d.,])" + Num + @"[\s_]*Gy(?![a-z])", Opt);
        private static readonly Regex CGyRule = new Regex(@"(?<![\d.,])(\d+)[\s_]*cGy(?![a-z])", Opt);
        private static readonly Regex FractionTokens = new Regex(
            @"(?<![a-z])x\s*\d+|\d+\s*#|\d+\s*(?:fx|fr|f)(?![a-z])|/\s*\d+", Opt);
        private static readonly Regex BareLarge = new Regex(@"(?<![\d.,])(\d{3,5})(?![\d.,])", Opt);
        private static readonly Regex GyLike = new Regex(@"(?<![\d.,])" + Num + @"[\s_]*G(?![a-z])", Opt);

        public const double MinGy = 1.0;
        public const double MaxGy = 100.0;

        /// <summary>Try to read a total prescription in Gy from a structure ID.</summary>
        public static bool TryParse(string? structureId, out double gy)
        {
            gy = 0;
            if (string.IsNullOrWhiteSpace(structureId)) return false;
            var s = structureId!;

            var m = FractionsTimesDose.Match(s);
            if (m.Success)
                return Accept(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * ParseNum(m.Groups[2].Value), out gy);

            m = GyRule.Match(s);
            if (m.Success) return Accept(ParseNum(m.Groups[1].Value), out gy);

            m = CGyRule.Match(s);
            if (m.Success) return Accept(ParseNum(m.Groups[1].Value) / 100.0, out gy);

            var stripped = FractionTokens.Replace(s, " ");

            m = BareLarge.Match(stripped);
            if (m.Success) return Accept(ParseNum(m.Groups[1].Value) / 100.0, out gy);

            m = GyLike.Match(stripped);
            if (m.Success)
            {
                double v = ParseNum(m.Groups[1].Value);
                if (v < 100) return Accept(v, out gy);
            }
            return false;
        }

        private static double ParseNum(string text) =>
            double.Parse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

        private static bool Accept(double value, out double gy)
        {
            gy = value;
            if (value >= MinGy && value <= MaxGy) return true;
            gy = 0;
            return false;
        }
    }

    /// <summary>Resolves a PTV prescription: name → matching RT prescription target → plan prescription.</summary>
    public static class PrescriptionResolver
    {
        /// <param name="ptvId">Structure ID.</param>
        /// <param name="prescriptionTargets">RT prescription target ID → total dose (Gy), if available.</param>
        /// <param name="planTotalGy">Plan prescription total dose (Gy), if any.</param>
        public static (double Gy, RxSource Source) Resolve(string ptvId,
            IReadOnlyDictionary<string, double>? prescriptionTargets, double? planTotalGy)
        {
            if (PrescriptionParser.TryParse(ptvId, out var gy)) return (gy, RxSource.FromName);
            if (prescriptionTargets != null)
                foreach (var kv in prescriptionTargets)
                    if (string.Equals(kv.Key, ptvId, StringComparison.OrdinalIgnoreCase) && kv.Value > 0)
                        return (kv.Value, RxSource.FromPrescription);
            if (planTotalGy is double p && p > 0) return (p, RxSource.PlanRx);
            return (0, RxSource.None);
        }
    }
}
