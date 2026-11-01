using System;
using System.Collections.Generic;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.Core.Optimisation
{
    /// <summary>Orientation preference for the recommendation (section 6.3).</summary>
    public enum OrientationPreference
    {
        NoPreference,
        /// <summary>Penalty per extra orientation used.</summary>
        PreferSame,
        /// <summary>Penalty per 270° plan.</summary>
        Prefer0,
        /// <summary>Penalty per 0° plan.</summary>
        Prefer270,
        /// <summary>Hard constraint: 0° only.</summary>
        Only0,
        /// <summary>Hard constraint: 270° only.</summary>
        Only270,
    }

    public static class OrientationPreferences
    {
        /// <summary>Orientations allowed by a preference (only the "Only" options restrict).</summary>
        public static IReadOnlyList<ArrayOrientation> Allowed(this OrientationPreference p) => p switch
        {
            OrientationPreference.Only0 => new[] { ArrayOrientation.Deg0 },
            OrientationPreference.Only270 => new[] { ArrayOrientation.Deg270 },
            _ => PlaneAxes.All,
        };

        /// <summary>Penalty for a set of chosen plane orientations (lower is better). Second key of the lexicographic objective.</summary>
        public static int Penalty(this OrientationPreference p, IEnumerable<ArrayOrientation> chosen)
        {
            int n0 = 0, n270 = 0;
            foreach (var o in chosen) if (o == ArrayOrientation.Deg0) n0++; else n270++;
            return p switch
            {
                OrientationPreference.PreferSame => Math.Max(0, (n0 > 0 ? 1 : 0) + (n270 > 0 ? 1 : 0) - 1),
                OrientationPreference.Prefer0 => n270,
                OrientationPreference.Prefer270 => n0,
                _ => 0,
            };
        }
    }

    /// <summary>All tunable optimisation parameters. Defaults follow the brief (section 6).</summary>
    public sealed class OptimisationSettings
    {
        /// <summary>Edge margin (mm) removed from each side of the 77 mm array to give the usable square.</summary>
        public double EdgeMarginMm { get; set; } = 3.0;

        /// <summary>Coverage threshold: PTV k is covered when J_k ≥ τ·J_k,best.</summary>
        public double Tau { get; set; } = 0.85;

        /// <summary>Minimum diodes inside a PTV for it to count as covered.</summary>
        public int MinDiodes { get; set; } = 5;

        /// <summary>α ∈ [0,1]: 1 = prefer high dose, 0 = prefer coverage (area).</summary>
        public double Alpha { get; set; } = 0.5;

        public OrientationPreference OrientationPreference { get; set; } = OrientationPreference.NoPreference;

        /// <summary>Pixel size of the in-plane sampling images (mm).</summary>
        public double PixelSizeMm { get; set; } = 0.5;

        /// <summary>Depth sweep step along the array normal (mm).</summary>
        public double DepthStepMm { get; set; } = 0.5;

        /// <summary>Coarse in-plane centre step (mm).</summary>
        public double CoarseStepMm { get; set; } = 1.0;

        /// <summary>Fine in-plane centre step for refinement (mm).</summary>
        public double FineStepMm { get; set; } = 0.5;

        /// <summary>Fine-tune search range in-plane (± mm).</summary>
        public double FineTuneInPlaneRangeMm { get; set; } = 15.0;

        /// <summary>Fine-tune search range along the normal (± mm).</summary>
        public double FineTuneNormalRangeMm { get; set; } = 10.0;

        public double FineTuneCoarseStepMm { get; set; } = 1.0;
        public double FineTuneFineStepMm { get; set; } = 0.25;

        /// <summary>Fraction of a PTV cross-section allowed outside the usable square before it is reported as clipped.</summary>
        public double ClipTolerance { get; set; } = 0.01;

        /// <summary>Upper bound on set-cover search nodes (safety net; normal cases use far fewer).</summary>
        public int MaxSearchNodes { get; set; } = 2_000_000;

        public double UsableHalfWidth => Detector.DiodeArray.UsableHalfWidth(EdgeMarginMm);

        public void Validate()
        {
            if (EdgeMarginMm < 0 || EdgeMarginMm >= Detector.DiodeArray.HalfExtent) throw new ArgumentOutOfRangeException(nameof(EdgeMarginMm));
            if (Tau <= 0 || Tau > 1) throw new ArgumentOutOfRangeException(nameof(Tau));
            if (Alpha < 0 || Alpha > 1) throw new ArgumentOutOfRangeException(nameof(Alpha));
            if (MinDiodes < 0) throw new ArgumentOutOfRangeException(nameof(MinDiodes));
            if (PixelSizeMm <= 0 || DepthStepMm <= 0 || CoarseStepMm <= 0 || FineStepMm <= 0) throw new ArgumentOutOfRangeException("step");
        }

        public OptimisationSettings Clone() => (OptimisationSettings)MemberwiseClone();
    }
}
