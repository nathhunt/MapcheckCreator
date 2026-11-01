using System;
using System.Collections.Generic;
using System.Linq;
using SrsMcPsqa.Core.Geometry;

namespace SrsMcPsqa.Core.Optimisation
{
    /// <summary>
    /// Limits on plan count and orientations for a constrained cover: <see cref="Total"/> plans, of which
    /// <see cref="Fixed0"/> must be 0° and <see cref="Fixed270"/> must be 270°; the rest are free.
    /// </summary>
    public sealed class SlotConstraint
    {
        public SlotConstraint(int total, int fixed0, int fixed270)
        {
            if (total < 0 || fixed0 < 0 || fixed270 < 0 || fixed0 + fixed270 > total) throw new ArgumentException("Invalid slot constraint.");
            Total = total; Fixed0 = fixed0; Fixed270 = fixed270;
        }

        public int Total { get; }
        public int Fixed0 { get; }
        public int Fixed270 { get; }
        public int Free => Total - Fixed0 - Fixed270;

        /// <summary>True when c0 plans at 0° and c270 at 270° can be placed into the slots.</summary>
        public bool Fits(int c0, int c270) =>
            c0 + c270 <= Total && Math.Max(0, c0 - Fixed0) + Math.Max(0, c270 - Fixed270) <= Free;
    }

    /// <summary>Result of <see cref="SetCoverSolver"/>.</summary>
    public sealed class CoverSolution
    {
        internal CoverSolution(IReadOnlyList<PlaneCandidate> chosen, ulong covered, ulong universe, int penalty, double value, bool hitNodeLimit)
        {
            Chosen = chosen; Covered = covered; Universe = universe; Penalty = penalty; Value = value; HitNodeLimit = hitNodeLimit;
        }

        public IReadOnlyList<PlaneCandidate> Chosen { get; }
        public ulong Covered { get; }
        public ulong Universe { get; }
        public bool Complete => (Covered & Universe) == Universe;
        public int Penalty { get; }

        /// <summary>Σ_k max over chosen planes of J_k.</summary>
        public double Value { get; }

        /// <summary>True if the search stopped at the node limit (result may not be optimal).</summary>
        public bool HitNodeLimit { get; }
    }

    /// <summary>
    /// Exact minimum set cover by depth-first branch and bound (section 6.3). Lexicographic objective:
    /// (1) fewest plans, (2) lowest orientation-preference penalty, (3) highest Σ_k max_planes J_k.
    /// Branching picks the uncovered PTV with the fewest covering candidates. Iterative deepening on plan count
    /// guarantees (1); all covers of the minimum size are compared for (2) and (3).
    /// </summary>
    public static class SetCoverSolver
    {
        public static CoverSolution Solve(IReadOnlyList<PlaneCandidate> options, ulong universe, OrientationPreference preference,
            int maxNodes = 2_000_000, SlotConstraint? slots = null)
        {
            int K = options.Count > 0 ? options[0].Scores.Length : 0;

            // PTVs no option can cover are removed from the universe (the caller reports them).
            ulong coverable = 0;
            foreach (var o in options) coverable |= o.Covered;
            ulong target = universe & coverable;
            if (target == 0) return new CoverSolution(Array.Empty<PlaneCandidate>(), 0, universe, 0, 0, false);

            var byElement = new List<int>[K];
            for (int k = 0; k < K; k++)
                byElement[k] = Enumerable.Range(0, options.Count).Where(i => (options[i].Covered >> k & 1UL) != 0)
                    .OrderByDescending(i => options[i].SetScore).ToList();
            int maxSetSize = options.Max(o => CandidateSearch.PopCount(o.Covered & target));
            int maxPlans = Math.Min(CandidateSearch.PopCount(target), slots?.Total ?? int.MaxValue);

            var state = new SearchState(options, preference, maxNodes, slots);
            for (int size = 1; size <= maxPlans && state.Best == null && !state.LimitHit; size++)
            {
                state.Size = size;
                Dfs(state, target, byElement, maxSetSize, new List<int>(), 0, 0);
            }

            if (state.Best == null)
                return GreedyMaxCoverage(options, target, universe, preference, slots, state.LimitHit);

            var chosen = state.Best.Select(i => options[i]).ToList();
            ulong covered = chosen.Aggregate(0UL, (a, c) => a | c.Covered);
            return new CoverSolution(chosen, covered, universe, state.BestPenalty, state.BestValue, state.LimitHit);
        }

        private sealed class SearchState
        {
            public SearchState(IReadOnlyList<PlaneCandidate> options, OrientationPreference pref, int maxNodes, SlotConstraint? slots)
            {
                Options = options; Preference = pref; MaxNodes = maxNodes; Slots = slots;
            }

            public IReadOnlyList<PlaneCandidate> Options { get; }
            public OrientationPreference Preference { get; }
            public int MaxNodes { get; }
            public SlotConstraint? Slots { get; }
            public int Size { get; set; }
            public int Nodes { get; set; }
            public bool LimitHit { get; set; }
            public int[]? Best { get; set; }
            public int BestPenalty { get; set; } = int.MaxValue;
            public double BestValue { get; set; } = double.NegativeInfinity;
            public HashSet<string> Seen { get; } = new HashSet<string>();
        }

        private static void Dfs(SearchState st, ulong uncovered, List<int>[] byElement, int maxSetSize, List<int> chosen, int c0, int c270)
        {
            if (st.LimitHit) return;
            if (++st.Nodes > st.MaxNodes) { st.LimitHit = true; return; }

            if (uncovered == 0) { Evaluate(st, chosen); return; }
            int remaining = st.Size - chosen.Count;
            if (remaining <= 0) return;
            if ((CandidateSearch.PopCount(uncovered) + maxSetSize - 1) / maxSetSize > remaining) return;

            // Branch on the uncovered PTV with the fewest options.
            int pick = -1, fewest = int.MaxValue;
            for (int k = 0; k < byElement.Length; k++)
            {
                if ((uncovered >> k & 1UL) == 0) continue;
                if (byElement[k].Count < fewest) { fewest = byElement[k].Count; pick = k; }
            }
            if (pick < 0 || fewest == 0) return;

            foreach (int i in byElement[pick])
            {
                var o = st.Options[i];
                int n0 = c0 + (o.Orientation == ArrayOrientation.Deg0 ? 1 : 0);
                int n270 = c270 + (o.Orientation == ArrayOrientation.Deg270 ? 1 : 0);
                if (st.Slots != null && !st.Slots.Fits(n0, n270)) continue;
                chosen.Add(i);
                Dfs(st, uncovered & ~o.Covered, byElement, maxSetSize, chosen, n0, n270);
                chosen.RemoveAt(chosen.Count - 1);
                if (st.LimitHit) return;
            }
        }

        private static void Evaluate(SearchState st, List<int> chosen)
        {
            var key = chosen.OrderBy(i => i).ToArray();
            if (!st.Seen.Add(string.Join(",", key))) return;
            var planes = key.Select(i => st.Options[i]).ToList();
            int penalty = st.Preference.Penalty(planes.Select(p => p.Orientation));
            double value = ValueOf(planes);
            bool better = st.Best == null || penalty < st.BestPenalty
                || (penalty == st.BestPenalty && value > st.BestValue + 1e-9)
                || (penalty == st.BestPenalty && Math.Abs(value - st.BestValue) <= 1e-9 && Compare(key, st.Best) < 0);
            if (!better) return;
            st.Best = key; st.BestPenalty = penalty; st.BestValue = value;
        }

        private static int Compare(int[] a, int[] b)
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }

        /// <summary>Σ_k max over the planes of J_k (objective 3).</summary>
        public static double ValueOf(IEnumerable<PlaneCandidate> planes)
        {
            double[]? max = null;
            foreach (var p in planes)
            {
                max ??= new double[p.Scores.Length];
                for (int k = 0; k < max.Length; k++) if (p.Scores[k] > max[k]) max[k] = p.Scores[k];
            }
            return max?.Sum() ?? 0;
        }

        /// <summary>Fallback when no full cover fits the slots: greedily maximise PTVs covered, then value.</summary>
        private static CoverSolution GreedyMaxCoverage(IReadOnlyList<PlaneCandidate> options, ulong target, ulong universe,
            OrientationPreference pref, SlotConstraint? slots, bool limitHit)
        {
            var chosen = new List<PlaneCandidate>();
            ulong covered = 0;
            int c0 = 0, c270 = 0;
            int maxPlans = slots?.Total ?? CandidateSearch.PopCount(target);
            while (chosen.Count < maxPlans && (covered & target) != target)
            {
                PlaneCandidate? pick = null; int pickGain = 0; double pickValue = double.NegativeInfinity;
                foreach (var o in options)
                {
                    if (chosen.Contains(o)) continue;
                    int n0 = c0 + (o.Orientation == ArrayOrientation.Deg0 ? 1 : 0), n270 = c270 + (o.Orientation == ArrayOrientation.Deg270 ? 1 : 0);
                    if (slots != null && !slots.Fits(n0, n270)) continue;
                    int gain = CandidateSearch.PopCount(o.Covered & target & ~covered);
                    if (gain == 0) continue;
                    double value = ValueOf(chosen.Append(o));
                    if (gain > pickGain || (gain == pickGain && value > pickValue + 1e-9)) { pick = o; pickGain = gain; pickValue = value; }
                }
                if (pick == null) break;
                chosen.Add(pick);
                covered |= pick.Covered;
                if (pick.Orientation == ArrayOrientation.Deg0) c0++; else c270++;
            }
            return new CoverSolution(chosen, covered, universe, pref.Penalty(chosen.Select(c => c.Orientation)), ValueOf(chosen), limitHit);
        }
    }
}
