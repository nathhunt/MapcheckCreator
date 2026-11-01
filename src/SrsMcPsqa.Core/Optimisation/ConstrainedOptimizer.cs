using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;

namespace SrsMcPsqa.Core.Optimisation
{
    /// <summary>User constraint for one QA plan: optional orientation and the PTVs the plane must include.</summary>
    public sealed class PlaneConstraint
    {
        public PlaneConstraint(ArrayOrientation? orientation = null, IEnumerable<int>? requiredTargets = null)
        {
            Orientation = orientation;
            RequiredTargets = (requiredTargets ?? Enumerable.Empty<int>()).Distinct().OrderBy(k => k).ToList();
        }

        public ArrayOrientation? Orientation { get; }
        public IReadOnlyList<int> RequiredTargets { get; }
    }

    /// <summary>
    /// User-constrained re-optimisation (section 6.4). The number of plans equals the number of constraints.
    /// <list type="number">
    /// <item>Planes with required PTVs: maximise Σ J_k over those PTVs, all of which must be covered. If that's impossible,
    /// maximise the minimum J_k and report it.</item>
    /// <item>PTVs not yet covered: constrained set cover over the remaining planes, honouring their fixed orientations.</item>
    /// <item>Planes still unused get the candidate that adds the most to Σ_k max J_k, so the user's plan count is kept.</item>
    /// </list>
    /// </summary>
    public static class ConstrainedOptimizer
    {
        public static Recommendation Optimise(PlanningModel model, CandidateAnalysis analysis, IReadOnlyList<PlaneConstraint> constraints)
        {
            if (constraints.Count == 0) throw new ArgumentException("At least one plane is required.", nameof(constraints));
            var s = analysis.Settings;
            var search = new CandidateSearch(model, s);
            var warnings = new List<string>();
            var slots = new PlaneCandidate?[constraints.Count];
            var allowed = analysis.Orientations;

            // 1. Planes with required PTVs.
            for (int i = 0; i < constraints.Count; i++)
            {
                var c = constraints[i];
                if (c.RequiredTargets.Count == 0) continue;
                var orients = c.Orientation is ArrayOrientation fixedO ? new[] { fixedO } : allowed.ToArray();
                var (cand, note) = OptimiseSinglePlane(model, search, analysis, orients, c.RequiredTargets);
                slots[i] = cand;
                if (note != null) warnings.Add($"Plan {i + 1}: {note}");
            }

            ulong covered = 0;
            foreach (var c in slots.Where(x => x != null))
                covered |= search.ApplyDiodeRule(c!, search.ScoreCoverage(c!, analysis.Reference), analysis.Reference);

            // 2. Cover the rest with the free planes.
            ulong universe = model.TargetCount == 64 ? ulong.MaxValue : (1UL << model.TargetCount) - 1;
            ulong remaining = universe & ~covered;
            var freeIdx = Enumerable.Range(0, constraints.Count).Where(i => slots[i] == null).ToList();
            if (remaining != 0 && freeIdx.Count > 0)
            {
                int f0 = freeIdx.Count(i => constraints[i].Orientation == ArrayOrientation.Deg0);
                int f270 = freeIdx.Count(i => constraints[i].Orientation == ArrayOrientation.Deg270);
                var options = analysis.CoverOptions.Where(o => allowed.Contains(o.Orientation)).ToList();
                var sol = SetCoverSolver.Solve(options, remaining, s.OrientationPreference, s.MaxSearchNodes,
                    new SlotConstraint(freeIdx.Count, f0, f270));
                if (sol.HitNodeLimit) warnings.Add("Search stopped at the node limit; the result may not be optimal.");
                AssignToSlots(sol.Chosen, freeIdx, constraints, slots);
            }

            // 3. Fill unused planes so the plan count is honoured.
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) continue;
                var want = constraints[i].Orientation;
                var current = slots.Where(x => x != null).Select(x => x!).ToList();
                double baseValue = SetCoverSolver.ValueOf(current);
                var pick = analysis.CoverOptions
                    .Where(o => (want == null || o.Orientation == want) && allowed.Contains(o.Orientation) && !current.Contains(o))
                    .Select(o => (o, gain: SetCoverSolver.ValueOf(current.Append(o)) - baseValue))
                    .OrderByDescending(x => x.gain).ThenByDescending(x => x.o.SetScore)
                    .Select(x => x.o).FirstOrDefault();
                if (pick == null)
                {
                    // Every cover option is already used (pruning can leave a single option). Use the best plane of the
                    // PTV that currently has the lowest score, so the extra plan adds a second, PTV-centred view.
                    double[] maxJ = new double[model.TargetCount];
                    foreach (var c in current) for (int k = 0; k < maxJ.Length; k++) maxJ[k] = Math.Max(maxJ[k], c.Scores[k]);
                    pick = analysis.BestPerTarget
                        .Select((c, k) => (c, k))
                        .Where(x => (want == null || x.c.Orientation == want) && !current.Any(y => y.Centre.ApproxEquals(x.c.Centre) && y.Orientation == x.c.Orientation))
                        .OrderBy(x => maxJ[x.k] / Math.Max(1e-9, analysis.Reference.BestScore[x.k])).ThenBy(x => x.k)
                        .Select(x => x.c).FirstOrDefault();
                }
                if (pick == null)
                {
                    var orients = want != null ? new[] { want.Value } : allowed.ToArray();
                    var all = Enumerable.Range(0, model.TargetCount).ToList();
                    pick = OptimiseSinglePlane(model, search, analysis, orients, all, maximiseOnly: true).Candidate;
                }
                slots[i] = pick;
                if (pick == null) warnings.Add($"Plan {i + 1}: no usable plane found.");
            }

            var planes = slots.Where(x => x != null).Select(x => new ArrayPlane(x!.Orientation, x.Centre));
            return Recommender.Build(model, analysis, planes, warnings, keepOrder: true);
        }

        /// <summary>Place solver-chosen planes into free slots: fixed-orientation slots first, then free ones.</summary>
        private static void AssignToSlots(IReadOnlyList<PlaneCandidate> chosen, List<int> freeIdx,
            IReadOnlyList<PlaneConstraint> constraints, PlaneCandidate?[] slots)
        {
            var pending = chosen.ToList();
            foreach (var i in freeIdx.Where(i => constraints[i].Orientation != null))
            {
                var c = pending.FirstOrDefault(p => p.Orientation == constraints[i].Orientation);
                if (c == null) continue;
                slots[i] = c; pending.Remove(c);
            }
            foreach (var i in freeIdx.Where(i => constraints[i].Orientation == null))
            {
                if (pending.Count == 0) break;
                slots[i] = pending[0]; pending.RemoveAt(0);
            }
        }

        /// <summary>
        /// Best single plane for a set of required PTVs. Returns the candidate and a note when the requirement can't be met.
        /// With <paramref name="maximiseOnly"/>, coverage isn't required: maximise Σ J_k.
        /// </summary>
        internal static (PlaneCandidate? Candidate, string? Note) OptimiseSinglePlane(PlanningModel model, CandidateSearch search,
            CandidateAnalysis analysis, IReadOnlyList<ArrayOrientation> orientations, IReadOnlyList<int> required, bool maximiseOnly = false)
        {
            var s = analysis.Settings;
            var reference = analysis.Reference;
            double half = s.UsableHalfWidth;
            ulong reqSet = required.Aggregate(0UL, (a, k) => a | 1UL << k);
            const int keep = 10;

            var feasible = new List<(PlaneCandidate c, double sum)>();
            PlaneCandidate? fallback = null; double fallbackMin = double.NegativeInfinity;

            foreach (var o in orientations)
            {
                int jMin = int.MaxValue, jMax = int.MinValue;
                int qu0 = int.MaxValue, qu1 = int.MinValue, qv0 = int.MaxValue, qv1 = int.MinValue;
                foreach (var k in required)
                {
                    var (f, l) = model.DepthRange(k, o);
                    if (l < f) continue;
                    jMin = Math.Min(jMin, f); jMax = Math.Max(jMax, l);
                    var r = search.CentreRange(k, o, s.CoarseStepMm);
                    qu0 = Math.Min(qu0, r.qu0); qu1 = Math.Max(qu1, r.qu1); qv0 = Math.Min(qv0, r.qv0); qv1 = Math.Max(qv1, r.qv1);
                }
                if (jMax < jMin) continue;

                var results = new (List<(int j, double cu, double cv, double sum)> top, (int j, double cu, double cv, double min) fb)[jMax - jMin + 1];
                Parallel.For(jMin, jMax + 1, j =>
                {
                    var top = new List<(int, double, double, double)>();
                    var fb = (j, 0.0, 0.0, double.NegativeInfinity);
                    for (int qv = qv0; qv <= qv1; qv++)
                        for (int qu = qu0; qu <= qu1; qu++)
                        {
                            double cu = model.U0(o) + qu * s.CoarseStepMm, cv = model.V0(o) + qv * s.CoarseStepMm;
                            double sum = 0, min = double.PositiveInfinity;
                            bool ok = true;
                            foreach (var k in required)
                            {
                                double sc = model.Sample(k, o, j, cu, cv, half, s.Alpha).Score;
                                sum += sc; min = Math.Min(min, sc);
                                if (!(reference.BestScore[k] > 0 && sc >= s.Tau * reference.BestScore[k] - 1e-12)) ok = false;
                            }
                            if (ok || maximiseOnly)
                            {
                                if (sum <= 0) continue;
                                top.Add((j, cu, cv, sum));
                                if (top.Count > keep * 4) { top = top.OrderByDescending(t => t.Item4).Take(keep).ToList(); }
                            }
                            else if (min > fb.Item4 + 1e-12) fb = (j, cu, cv, min);
                        }
                    results[j - jMin] = (top.OrderByDescending(t => t.Item4).Take(keep).ToList(), fb);
                });

                foreach (var r in results)
                {
                    foreach (var t in r.top) feasible.Add((search.MakeCandidate(o, t.j, t.cu, t.cv, 0), t.sum));
                    if (r.fb.min > fallbackMin + 1e-12 && r.fb.min > double.NegativeInfinity)
                    {
                        fallbackMin = r.fb.min;
                        fallback = search.MakeCandidate(o, r.fb.j, r.fb.cu, r.fb.cv, 0);
                    }
                }
            }

            var ordered = feasible.OrderByDescending(f => f.sum).ThenBy(f => f.c.Orientation).ThenBy(f => f.c.DepthIndex)
                .ThenBy(f => f.c.Cu).ThenBy(f => f.c.Cv).Take(keep * 4).ToList();
            if (maximiseOnly)
                return (ordered.Count > 0 ? ordered[0].c : null, null);

            foreach (var (c, _) in ordered)
            {
                if ((search.ApplyDiodeRule(c, reqSet, reference) & reqSet) != reqSet) continue;
                c.SetCovered(reqSet);
                return (search.CentreOnTargets(Refine(model, search, analysis, c, reqSet), reqSet, reference), null);
            }
            if (ordered.Count > 0)
            {
                ordered[0].c.SetCovered(reqSet);
                return (ordered[0].c, $"required PTVs reach the score threshold but not all have {s.MinDiodes} diodes inside.");
            }
            if (fallback != null)
            {
                var names = string.Join(", ", required.Select(k => model.Targets[k].Id));
                return (fallback, $"cannot cover all of {names} on one plane; maximised the lowest PTV score instead.");
            }
            return (null, "no plane intersects the required PTVs.");
        }

        private static PlaneCandidate Refine(PlanningModel model, CandidateSearch search, CandidateAnalysis analysis, PlaneCandidate c, ulong reqSet)
        {
            var s = analysis.Settings;
            double step = s.FineStepMm;
            int n = (int)Math.Round(s.CoarseStepMm / step);
            var best = c;
            for (int a = -n + 1; a <= n - 1; a++)
                for (int b = -n + 1; b <= n - 1; b++)
                {
                    if (a == 0 && b == 0) continue;
                    var t = search.MakeCandidate(c.Orientation, c.DepthIndex, c.Cu + a * step, c.Cv + b * step, 0);
                    if ((search.ScoreCoverage(t, analysis.Reference) & reqSet) != reqSet) continue;
                    if (PlaneCandidate.SumOver(t.Scores, reqSet) <= best.SetScore + 1e-9) continue;
                    if ((search.ApplyDiodeRule(t, reqSet, analysis.Reference) & reqSet) != reqSet) continue;
                    t.SetCovered(reqSet);
                    best = t;
                }
            return best;
        }
    }
}
