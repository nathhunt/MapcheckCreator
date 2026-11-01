using System;
using System.Collections.Generic;
using System.Linq;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;

namespace SrsMcPsqa.Core.Optimisation
{
    /// <summary>One recommended QA plan: its array plane and the metrics of every PTV on it.</summary>
    public sealed class RecommendedPlane
    {
        public RecommendedPlane(PlaneEvaluation evaluation, IReadOnlyList<int> primaryTargets)
        {
            Evaluation = evaluation; PrimaryTargets = primaryTargets;
        }

        public ArrayPlane Plane => Evaluation.Plane;
        public PlaneEvaluation Evaluation { get; }

        /// <summary>Indices of PTVs covered on this plane.</summary>
        public IReadOnlyList<int> CoveredTargets => Evaluation.Targets.Where(t => t.Covered).Select(t => t.TargetIndex).ToList();

        /// <summary>Indices of PTVs whose best plane this is (each PTV counts toward one primary plane).</summary>
        public IReadOnlyList<int> PrimaryTargets { get; }

        /// <summary>Σ J_k over the PTVs covered on this plane.</summary>
        public double Score => Evaluation.Targets.Where(t => t.Covered).Sum(t => t.Score);
    }

    /// <summary>Coverage summary for one PTV across all planes of a recommendation.</summary>
    public sealed class TargetCoverage
    {
        public int TargetIndex { get; set; }
        public string TargetId { get; set; } = "";
        public CoverageStatus Status { get; set; }

        /// <summary>Index into <see cref="Recommendation.Planes"/> of this PTV's best plane, or null if it isn't on any plane.</summary>
        public int? PrimaryPlane { get; set; }
        public double BestScore { get; set; }
    }

    /// <summary>Recommendation or constrained re-optimisation result. Pre-calculation values are approximations.</summary>
    public sealed class Recommendation
    {
        public Recommendation(IReadOnlyList<RecommendedPlane> planes, IReadOnlyList<TargetCoverage> targets, IReadOnlyList<string> warnings)
        {
            Planes = planes; Targets = targets; Warnings = warnings;
        }

        public IReadOnlyList<RecommendedPlane> Planes { get; }
        public IReadOnlyList<TargetCoverage> Targets { get; }
        public IReadOnlyList<string> Warnings { get; }
        public bool AllCovered => Targets.All(t => t.Status != CoverageStatus.NotCovered);
    }

    /// <summary>
    /// Recommends the minimum number of QA plans and the array plane for each (section 6.3).
    /// Typical use: <c>var analysis = new CandidateSearch(model, settings).Analyse(); var rec = Recommender.Recommend(model, analysis);</c>
    /// Keep the analysis for constrained re-optimisation and manual edits.
    /// </summary>
    public static class Recommender
    {
        public static Recommendation Recommend(PlanningModel model, OptimisationSettings settings) =>
            Recommend(model, new CandidateSearch(model, settings).Analyse());

        public static Recommendation Recommend(PlanningModel model, CandidateAnalysis analysis)
        {
            var s = analysis.Settings;
            ulong universe = model.TargetCount == 64 ? ulong.MaxValue : (1UL << model.TargetCount) - 1;
            var solution = SetCoverSolver.Solve(analysis.CoverOptions, universe, s.OrientationPreference, s.MaxSearchNodes);
            var warnings = new List<string>();
            if (solution.HitNodeLimit) warnings.Add("Search stopped at the node limit; the recommendation may not be optimal.");
            var planes = solution.Chosen.Select(c => new ArrayPlane(c.Orientation, c.Centre));
            return Build(model, analysis, planes, warnings);
        }

        /// <summary>
        /// Evaluate a set of planes (recommended, constrained or manually edited) and summarise coverage per PTV.
        /// </summary>
        /// <param name="keepOrder">Keep the given plane order (constrained slots); otherwise sort by orientation, then depth.</param>
        public static Recommendation Build(PlanningModel model, CandidateAnalysis analysis, IEnumerable<ArrayPlane> planes,
            IList<string>? warnings = null, bool keepOrder = false)
        {
            var s = analysis.Settings;
            var evals = planes.Select(p => model.Evaluate(p, s, analysis.Reference)).ToList();
            if (!keepOrder) evals = evals.OrderBy(e => e.Plane.Orientation).ThenBy(e => e.Plane.Depth).ToList();
            var warn = warnings ?? new List<string>();

            var primary = new List<int>[evals.Count];
            for (int i = 0; i < evals.Count; i++) primary[i] = new List<int>();
            var targets = new List<TargetCoverage>();

            for (int k = 0; k < model.TargetCount; k++)
            {
                int? best = null;
                double bestScore = 0;
                bool anyCovered = evals.Any(e => e.Targets[k].Covered);
                for (int i = 0; i < evals.Count; i++)
                {
                    var m = evals[i].Targets[k];
                    if (anyCovered && !m.Covered) continue;
                    if (m.Score > bestScore + 1e-12) { best = i; bestScore = m.Score; }
                }

                CoverageStatus status;
                if (!anyCovered) status = CoverageStatus.NotCovered;
                else if (best != null && evals[best.Value].Targets[k].Clipped) status = CoverageStatus.Partial;
                else if (analysis.Reference.DiodeRuleWaived[k]) status = CoverageStatus.LowDiodeCount;
                else status = CoverageStatus.Covered;

                if (best != null) primary[best.Value].Add(k);
                var id = model.Targets[k].Id;
                targets.Add(new TargetCoverage
                {
                    TargetIndex = k, TargetId = id, Status = status, PrimaryPlane = best,
                    BestScore = analysis.Reference.BestScore[k],
                });
                switch (status)
                {
                    case CoverageStatus.NotCovered: warn.Add($"{id}: not covered by any plane."); break;
                    case CoverageStatus.Partial: warn.Add($"{id}: partial coverage (cross-section extends beyond the usable array area)."); break;
                    case CoverageStatus.LowDiodeCount: warn.Add($"{id}: fewer than {s.MinDiodes} diodes inside the PTV on any plane."); break;
                }
            }

            var planesOut = evals.Select((e, i) => new RecommendedPlane(e, primary[i])).ToList();
            return new Recommendation(planesOut, targets, warn.ToList());
        }
    }
}
