using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.Core.Optimisation
{
    /// <summary>A candidate array plane on the model grid, with J_k for every PTV and the set of PTVs it covers.</summary>
    public sealed class PlaneCandidate
    {
        internal PlaneCandidate(ArrayOrientation orientation, int depthIndex, double cu, double cv, Vec3 centre, double[] scores, ulong covered)
        {
            Orientation = orientation; DepthIndex = depthIndex; Cu = cu; Cv = cv; Centre = centre; Scores = scores; Covered = covered;
            SetScore = SumOver(scores, covered);
        }

        public ArrayOrientation Orientation { get; }
        public int DepthIndex { get; }
        public double Cu { get; }
        public double Cv { get; }

        /// <summary>Patient point on the array centre.</summary>
        public Vec3 Centre { get; }

        /// <summary>J_k for every PTV in the model (0 if not intersected).</summary>
        public double[] Scores { get; }

        /// <summary>Bit k set = PTV k covered.</summary>
        public ulong Covered { get; internal set; }

        /// <summary>Σ J_k over the covered PTVs.</summary>
        public double SetScore { get; private set; }

        internal void SetCovered(ulong covered) { Covered = covered; SetScore = SumOver(Scores, covered); }

        internal static double SumOver(double[] scores, ulong set)
        {
            double s = 0;
            for (int k = 0; k < scores.Length; k++) if ((set >> k & 1UL) != 0) s += scores[k];
            return s;
        }

        /// <summary>Deterministic ordering: higher score first, ties broken by position.</summary>
        internal bool IsBetterThan(PlaneCandidate other, double score, double otherScore)
        {
            if (score > otherScore + 1e-9) return true;
            if (score < otherScore - 1e-9) return false;
            int c = Orientation.CompareTo(other.Orientation);
            if (c != 0) return c < 0;
            if (DepthIndex != other.DepthIndex) return DepthIndex < other.DepthIndex;
            if (Cu != other.Cu) return Cu < other.Cu;
            return Cv < other.Cv;
        }

        public override string ToString() => $"{Orientation.Label()} {Centre} set=0x{Covered:X} score={SetScore:0.###}";
    }

    /// <summary>Result of the candidate sweep, reused by the recommendation and the constrained re-optimisation.</summary>
    public sealed class CandidateAnalysis
    {
        internal CandidateAnalysis(OptimisationSettings settings, IReadOnlyList<ArrayOrientation> orientations,
            CoverageReference reference, PlaneCandidate[] bestPerTarget, IReadOnlyList<PlaneCandidate> coverOptions)
        {
            Settings = settings; Orientations = orientations; Reference = reference; BestPerTarget = bestPerTarget; CoverOptions = coverOptions;
        }

        public OptimisationSettings Settings { get; }
        public IReadOnlyList<ArrayOrientation> Orientations { get; }
        public CoverageReference Reference { get; }

        /// <summary>Location of J_k,best for each PTV.</summary>
        public PlaneCandidate[] BestPerTarget { get; }

        /// <summary>Best candidate for each distinct (covered set, orientation), after diode check, refinement and dominance pruning.</summary>
        public IReadOnlyList<PlaneCandidate> CoverOptions { get; }
    }

    /// <summary>
    /// Sweeps candidate planes (section 6.2): depth in model steps, in-plane centres on a coarse grid,
    /// refinement on a fine grid. Uses the model's summed-area tables, so each PTV evaluation is O(1).
    /// </summary>
    public sealed class CandidateSearch
    {
        private readonly PlanningModel _m;
        private readonly OptimisationSettings _s;
        private readonly double _half;

        public CandidateSearch(PlanningModel model, OptimisationSettings settings)
        {
            settings.Validate();
            _m = model; _s = settings; _half = settings.UsableHalfWidth;
        }

        /// <summary>Run both passes: J_k,best per PTV, then the covered sets.</summary>
        public CandidateAnalysis Analyse(IReadOnlyList<ArrayOrientation>? orientations = null)
        {
            var orients = orientations ?? _s.OrientationPreference.Allowed();
            var reference = ComputeReference(orients, out var best);
            var options = CollectCoverSets(reference, orients, best);
            return new CandidateAnalysis(_s.Clone(), orients, reference, best, options);
        }

        // ------------------------------------------------------------------------------ pass 1: J_k,best
        /// <summary>J_k,best for each PTV alone over every plane of the allowed orientations, and whether the diode rule must be waived.</summary>
        public CoverageReference ComputeReference(IReadOnlyList<ArrayOrientation> orientations, out PlaneCandidate[] bestPerTarget)
        {
            int K = _m.TargetCount;
            var best = new PlaneCandidate[K];
            var bestScore = new double[K];
            var waived = new bool[K];

            for (int k = 0; k < K; k++)
            {
                PlaneCandidate? b = null; double bs = -1;
                foreach (var o in orientations)
                {
                    var (first, last) = _m.DepthRange(k, o);
                    if (last < first) continue;
                    var (qu0, qu1, qv0, qv1) = CentreRange(k, o, _s.CoarseStepMm);
                    var locals = new (int j, double cu, double cv, double score)[last - first + 1];
                    int kk = k;
                    Parallel.For(first, last + 1, j =>
                    {
                        var local = (j, 0.0, 0.0, -1.0);
                        for (int qv = qv0; qv <= qv1; qv++)
                            for (int qu = qu0; qu <= qu1; qu++)
                            {
                                double cu = _m.U0(o) + qu * _s.CoarseStepMm, cv = _m.V0(o) + qv * _s.CoarseStepMm;
                                double sc = _m.Sample(kk, o, j, cu, cv, _half, _s.Alpha).Score;
                                if (sc > local.Item4 + 1e-12) local = (j, cu, cv, sc);
                            }
                        locals[j - first] = local;
                    });
                    foreach (var l in locals)
                    {
                        if (l.score <= 0) continue;
                        var c = MakeCandidate(o, l.j, l.cu, l.cv, 0);
                        double sc = c.Scores[k];
                        if (b == null || c.IsBetterThan(b, sc, bs)) { b = c; bs = sc; }
                    }
                }
                if (b == null) throw new InvalidOperationException($"PTV '{_m.Targets[k].Id}' has no cross-section on any allowed plane.");

                // Fine refinement around the best coarse location.
                b = RefineSingle(k, b);
                b = CentreOnTargets(b, 1UL << k, null);
                best[k] = b;
                bestScore[k] = b.Scores[k];

                // Diode rule: if no position near the best gives N_min diodes, waive it for this PTV.
                waived[k] = !CanReachMinDiodes(k, b);
            }

            for (int k = 0; k < K; k++)
                best[k].SetCovered(1UL << k);
            bestPerTarget = best;
            return new CoverageReference(bestScore, waived);
        }

        private PlaneCandidate RefineSingle(int k, PlaneCandidate c)
        {
            double step = _s.FineStepMm, range = _s.CoarseStepMm;
            int n = (int)Math.Round(range / step);
            var best = c; double bs = c.Scores[k];
            for (int dj = -1; dj <= 1; dj++)
                for (int a = -n; a <= n; a++)
                    for (int b = -n; b <= n; b++)
                    {
                        if (a == 0 && b == 0 && dj == 0) continue;
                        double cu = c.Cu + a * step, cv = c.Cv + b * step;
                        double sc = _m.Sample(k, c.Orientation, c.DepthIndex + dj, cu, cv, _half, _s.Alpha).Score;
                        if (sc > bs + 1e-12) { best = MakeCandidate(c.Orientation, c.DepthIndex + dj, cu, cv, 0); bs = sc; }
                    }
            return best;
        }

        private bool CanReachMinDiodes(int k, PlaneCandidate b)
        {
            if (_s.MinDiodes <= 0) return true;
            var o = b.Orientation;
            if (_m.CountDiodesInside(k, o, b.Centre) >= _s.MinDiodes) return true;
            // Lattice alignment matters for small PTVs: try small in-plane shifts.
            for (double du = -1.5; du <= 1.5 + 1e-9; du += 0.25)
                for (double dv = -1.5; dv <= 1.5 + 1e-9; dv += 0.25)
                {
                    var p = o.FromPlane(b.Cu + du, b.Cv + dv, _m.DepthOf(o, b.DepthIndex));
                    if (_m.CountDiodesInside(k, o, p) >= _s.MinDiodes) return true;
                }
            return false;
        }

        // ------------------------------------------------------------------------------ pass 2: covered sets
        /// <summary>
        /// Best candidate for each distinct covered set and orientation. Coverage is first judged by score (J_k ≥ τ·J_k,best) during the
        /// sweep. The diode rule is then applied to each set's best candidate, which may shrink its set. Finally candidates
        /// are refined on the fine grid and dominated sets are pruned.
        /// </summary>
        public IReadOnlyList<PlaneCandidate> CollectCoverSets(CoverageReference reference, IReadOnlyList<ArrayOrientation> orientations,
            PlaneCandidate[] bestPerTarget)
        {
            int K = _m.TargetCount;
            double tau = _s.Tau;
            var merged = new Dictionary<(ulong, ArrayOrientation), PlaneCandidate>();

            foreach (var o in orientations)
            {
                int jMin = int.MaxValue, jMax = int.MinValue;
                for (int k = 0; k < K; k++)
                {
                    var (f, l) = _m.DepthRange(k, o);
                    if (l < f) continue;
                    jMin = Math.Min(jMin, f); jMax = Math.Max(jMax, l);
                }
                if (jMax < jMin) continue;

                var ranges = new (int qu0, int qu1, int qv0, int qv1)[K];
                for (int k = 0; k < K; k++) ranges[k] = CentreRange(k, o, _s.CoarseStepMm);

                var perDepth = new Dictionary<ulong, PlaneCandidate>[jMax - jMin + 1];
                Parallel.For(jMin, jMax + 1, j =>
                {
                    var local = new Dictionary<ulong, PlaneCandidate>();
                    var present = new List<int>();
                    for (int k = 0; k < K; k++)
                    {
                        var (f, l) = _m.DepthRange(k, o);
                        if (j >= f && j <= l) present.Add(k);
                    }
                    if (present.Count == 0) { perDepth[j - jMin] = local; return; }

                    int qu0 = present.Min(k => ranges[k].qu0), qu1 = present.Max(k => ranges[k].qu1);
                    int qv0 = present.Min(k => ranges[k].qv0), qv1 = present.Max(k => ranges[k].qv1);
                    var scores = new double[K];
                    for (int qv = qv0; qv <= qv1; qv++)
                        for (int qu = qu0; qu <= qu1; qu++)
                        {
                            ulong covered = 0;
                            double setScore = 0;
                            double cu = _m.U0(o) + qu * _s.CoarseStepMm, cv = _m.V0(o) + qv * _s.CoarseStepMm;
                            foreach (var k in present)
                            {
                                var r = ranges[k];
                                if (qu < r.qu0 || qu > r.qu1 || qv < r.qv0 || qv > r.qv1) { scores[k] = 0; continue; }
                                double sc = _m.Sample(k, o, j, cu, cv, _half, _s.Alpha).Score;
                                scores[k] = sc;
                                if (reference.BestScore[k] > 0 && sc >= tau * reference.BestScore[k] - 1e-12) { covered |= 1UL << k; setScore += sc; }
                            }
                            if (covered == 0) continue;
                            if (local.TryGetValue(covered, out var existing) &&
                                !(setScore > existing.SetScore + 1e-9 ||
                                  (Math.Abs(setScore - existing.SetScore) <= 1e-9 && TieBreak(j, cu, cv, existing))))
                                continue;
                            var copy = new double[K];
                            Array.Copy(scores, copy, K);
                            local[covered] = new PlaneCandidate(o, j, cu, cv, _m.PointAt(o, j, cu, cv), copy, covered);
                            for (int k = 0; k < K; k++) scores[k] = 0;
                        }
                    perDepth[j - jMin] = local;
                });

                foreach (var d in perDepth)
                    foreach (var kv in d)
                        Offer(merged, kv.Value);
            }

            // Seeds: each PTV's best location, so every PTV is coverable by at least one candidate.
            foreach (var b in bestPerTarget)
            {
                var c = MakeCandidate(b.Orientation, b.DepthIndex, b.Cu, b.Cv, 0);
                c.SetCovered(ScoreCoverage(c, reference));
                if (c.Covered != 0) Offer(merged, c);
            }

            // Diode rule, then re-key.
            var checkedSets = new Dictionary<(ulong, ArrayOrientation), PlaneCandidate>();
            foreach (var c in merged.Values)
            {
                c.SetCovered(ApplyDiodeRule(c, c.Covered, reference));
                if (c.Covered != 0) Offer(checkedSets, c);
            }

            // Refine each set's best on the fine grid, keeping its covered set.
            var refined = checkedSets.Values.Select(c => CentreOnTargets(RefineSet(c, reference), c.Covered, reference)).ToList();
            return PruneDominated(refined);
        }

        private static bool TieBreak(int j, double cu, double cv, PlaneCandidate existing)
        {
            if (j != existing.DepthIndex) return j < existing.DepthIndex;
            if (cu != existing.Cu) return cu < existing.Cu;
            return cv < existing.Cv;
        }

        /// <summary>Keep the best candidate per (covered set, orientation): orientation matters for the preference objective.</summary>
        private static void Offer(Dictionary<(ulong, ArrayOrientation), PlaneCandidate> dict, PlaneCandidate c)
        {
            var key = (c.Covered, c.Orientation);
            if (!dict.TryGetValue(key, out var e) || c.IsBetterThan(e, c.SetScore, e.SetScore)) dict[key] = c;
        }

        /// <summary>Set of PTVs whose score meets τ·J_best (no diode rule).</summary>
        internal ulong ScoreCoverage(PlaneCandidate c, CoverageReference reference)
        {
            ulong set = 0;
            for (int k = 0; k < c.Scores.Length; k++)
                if (reference.BestScore[k] > 0 && c.Scores[k] >= _s.Tau * reference.BestScore[k] - 1e-12) set |= 1UL << k;
            return set;
        }

        /// <summary>Remove PTVs from <paramref name="set"/> that have fewer than N_min diodes inside (unless waived).</summary>
        internal ulong ApplyDiodeRule(PlaneCandidate c, ulong set, CoverageReference reference)
        {
            if (_s.MinDiodes <= 0) return set;
            for (int k = 0; k < c.Scores.Length; k++)
            {
                if ((set >> k & 1UL) == 0 || reference.DiodeRuleWaived[k]) continue;
                if (_m.CountDiodesInside(k, c.Orientation, c.Centre) < _s.MinDiodes) set &= ~(1UL << k);
            }
            return set;
        }

        private PlaneCandidate RefineSet(PlaneCandidate c, CoverageReference reference)
        {
            double step = _s.FineStepMm;
            int n = (int)Math.Round(_s.CoarseStepMm / step);
            if (n <= 1 && step >= _s.CoarseStepMm) return c;
            var best = c;
            for (int a = -n + 1; a <= n - 1; a++)
                for (int b = -n + 1; b <= n - 1; b++)
                {
                    if (a == 0 && b == 0) continue;
                    var t = MakeCandidate(c.Orientation, c.DepthIndex, c.Cu + a * step, c.Cv + b * step, 0);
                    ulong scoreSet = ScoreCoverage(t, reference);
                    if ((scoreSet & c.Covered) != c.Covered) continue;
                    double sc = PlaneCandidate.SumOver(t.Scores, c.Covered);
                    if (sc <= best.SetScore + 1e-9) continue;
                    if ((ApplyDiodeRule(t, c.Covered, reference) & c.Covered) != c.Covered) continue;
                    t.SetCovered(c.Covered);
                    best = t;
                }
            return best;
        }

        /// <summary>
        /// Drop a candidate when another of the same orientation covers a strict superset with J_k at least as high for
        /// every PTV of the smaller set. Such a candidate can never improve any objective.
        /// </summary>
        internal static List<PlaneCandidate> PruneDominated(List<PlaneCandidate> cands)
        {
            var sorted = cands.OrderByDescending(c => PopCount(c.Covered)).ThenByDescending(c => c.SetScore)
                .ThenBy(c => c.Orientation).ThenBy(c => c.DepthIndex).ThenBy(c => c.Cu).ThenBy(c => c.Cv).ToList();
            var kept = new List<PlaneCandidate>();
            foreach (var c in sorted)
            {
                bool dominated = kept.Any(b => b.Orientation == c.Orientation && b.Covered != c.Covered
                    && (b.Covered & c.Covered) == c.Covered && AllAtLeast(b, c));
                if (!dominated) kept.Add(c);
            }
            return kept;
        }

        private static bool AllAtLeast(PlaneCandidate b, PlaneCandidate c)
        {
            for (int k = 0; k < c.Scores.Length; k++)
                if ((c.Covered >> k & 1UL) != 0 && b.Scores[k] < c.Scores[k] - 1e-12) return false;
            return true;
        }

        internal static int PopCount(ulong x)
        {
            int n = 0;
            while (x != 0) { x &= x - 1; n++; }
            return n;
        }

        /// <summary>
        /// Many in-plane positions score the same once every covered PTV is fully inside the usable square.
        /// Among those, move the array towards the centre of the covered PTVs' cross-sections at this depth (the area
        /// the array actually intersects), so it sits mid-array with the most margin. Accepts a move only if no covered score drops and the set stays covered.
        /// </summary>
        /// <param name="reference">Null for a single-PTV best location (only the score is checked).</param>
        internal PlaneCandidate CentreOnTargets(PlaneCandidate c, ulong set, CoverageReference? reference)
        {
            var o = c.Orientation;
            // Centre on the cross-sections the array actually intersects at this depth (not the 3D bounding boxes).
            var members = Enumerable.Range(0, _m.TargetCount).Where(k => (set >> k & 1UL) != 0);
            var centre = _m.CrossSectionCentre(members, o, c.DepthIndex);
            if (centre == null) return c;
            double step = _s.FineStepMm;
            double Snap(double x, double origin) => origin + Math.Round((x - origin) / step) * step;
            double tu = Snap(centre.Value.U, _m.U0(o)), tv = Snap(centre.Value.V, _m.V0(o));

            // Try the full move first, then shorter moves along the same line.
            foreach (var f in new[] { 1.0, 0.75, 0.5, 0.25 })
            {
                double cu = Snap(c.Cu + f * (tu - c.Cu), _m.U0(o)), cv = Snap(c.Cv + f * (tv - c.Cv), _m.V0(o));
                if (cu == c.Cu && cv == c.Cv) return c;
                var t = MakeCandidate(o, c.DepthIndex, cu, cv, 0);
                bool ok = true;
                for (int k = 0; k < t.Scores.Length && ok; k++)
                    if ((set >> k & 1UL) != 0 && t.Scores[k] < c.Scores[k] - 1e-9) ok = false;
                if (!ok) continue;
                if (reference != null)
                {
                    if ((ScoreCoverage(t, reference) & set) != set) continue;
                    if ((ApplyDiodeRule(t, set, reference) & set) != set) continue;
                }
                t.SetCovered(c.Covered);
                return t;
            }
            return c;
        }

        // ------------------------------------------------------------------------------ helpers
        /// <summary>Candidate with J_k for all PTVs at a grid position.</summary>
        internal PlaneCandidate MakeCandidate(ArrayOrientation o, int j, double cu, double cv, ulong covered)
        {
            var scores = new double[_m.TargetCount];
            for (int k = 0; k < scores.Length; k++) scores[k] = _m.Sample(k, o, j, cu, cv, _half, _s.Alpha).Score;
            return new PlaneCandidate(o, j, cu, cv, _m.PointAt(o, j, cu, cv), scores, covered);
        }

        /// <summary>Coarse centre index range (u and v) for which the usable square can intersect PTV k.</summary>
        internal (int qu0, int qu1, int qv0, int qv1) CentreRange(int k, ArrayOrientation o, double step)
        {
            var (umin, umax, vmin, vmax) = _m.InPlaneBounds(k, o);
            double u0 = _m.U0(o), v0 = _m.V0(o);
            return ((int)Math.Ceiling((umin - _half - u0) / step), (int)Math.Floor((umax + _half - u0) / step),
                    (int)Math.Ceiling((vmin - _half - v0) / step), (int)Math.Floor((vmax + _half - v0) / step));
        }
    }
}
