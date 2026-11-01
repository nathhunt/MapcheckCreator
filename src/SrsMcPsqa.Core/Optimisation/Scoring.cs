using System;

namespace SrsMcPsqa.Core.Optimisation
{
    /// <summary>
    /// Per-PTV score of an array plane (section 6.1). This is the one function to tune.
    /// <code>
    /// J_k = α·[0.5·(H_k / A_k,max) + 0.5·M_k] + (1 − α)·(A_k / A_k,max)
    /// </code>
    /// <list type="bullet">
    /// <item><c>A_k</c>: PTV cross-section area inside the usable square (mm²).</item>
    /// <item><c>A_k,max</c>: largest cross-section of PTV k over all depths for this orientation (mm²).</item>
    /// <item><c>H_k</c>: area inside the usable square and the PTV where D ≥ Rx_k (mm²).</item>
    /// <item><c>M_k</c>: mean of min(D/Rx_k, 1.2) over the PTV area inside the usable square.</item>
    /// <item><c>α</c> ∈ [0,1]: 1 = prefer high dose, 0 = prefer coverage.</item>
    /// </list>
    /// J is 0 when the PTV does not intersect the square. For a plane through the widest part of a PTV that is fully
    /// inside the square and fully at or above Rx, J = α·(0.5 + 0.5·M) + (1 − α) ≈ 1.
    /// </summary>
    public static class Scoring
    {
        /// <summary>Clamp applied to D/Rx when averaging, so hot spots can't dominate M_k.</summary>
        public const double RatioCap = 1.2;

        public static double PtvScore(double alpha, double areaInSquare, double maxArea, double highDoseArea, double meanDoseRatio)
        {
            if (maxArea <= 0 || areaInSquare <= 0) return 0;
            double a = Math.Min(1.0, areaInSquare / maxArea);
            double h = Math.Min(1.0, highDoseArea / maxArea);
            return alpha * (0.5 * h + 0.5 * meanDoseRatio) + (1 - alpha) * a;
        }

        /// <summary>Coverage rule: J_k ≥ τ·J_k,best and (unless waived) N_k ≥ N_min.</summary>
        public static bool IsCovered(double j, double jBest, double tau, int diodes, int minDiodes, bool diodeRuleWaived) =>
            jBest > 0 && j >= tau * jBest - 1e-12 && (diodeRuleWaived || diodes >= minDiodes);
    }
}
