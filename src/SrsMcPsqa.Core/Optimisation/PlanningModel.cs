using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SrsMcPsqa.Core.Detector;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;

namespace SrsMcPsqa.Core.Optimisation
{
    /// <summary>Raw per-PTV quantities for one candidate plane (before diode counting).</summary>
    public readonly struct PtvSample
    {
        public PtvSample(double areaInSquare, double sliceArea, double maxArea, double highDoseArea, double meanRatio, double score)
        {
            AreaInSquare = areaInSquare; SliceArea = sliceArea; MaxArea = maxArea;
            HighDoseArea = highDoseArea; MeanRatio = meanRatio; Score = score;
        }

        public double AreaInSquare { get; }
        public double SliceArea { get; }
        public double MaxArea { get; }
        public double HighDoseArea { get; }
        public double MeanRatio { get; }
        public double Score { get; }

        /// <summary>Fraction of the cross-section at this depth outside the usable square.</summary>
        public double ClippedFraction => SliceArea > 0 ? 1 - AreaInSquare / SliceArea : 0;
    }

    /// <summary>Reference values needed to judge coverage: J_k,best and whether the diode rule is waived per PTV.</summary>
    public sealed class CoverageReference
    {
        public CoverageReference(double[] bestScore, bool[] diodeRuleWaived)
        {
            BestScore = bestScore; DiodeRuleWaived = diodeRuleWaived;
        }

        public double[] BestScore { get; }
        public bool[] DiodeRuleWaived { get; }
    }

    /// <summary>
    /// Pre-computed sampling of PTV masks and dose on array planes, for fast candidate evaluation (section 6.2).
    /// <para>
    /// For each orientation, depth (along the normal, step <see cref="DepthStep"/>) and PTV, three in-plane images with
    /// <see cref="PixelSize"/> pixels are built over the PTV's in-plane bounding box: PTV mask, PTV ∧ (D ≥ Rx_k) and
    /// PTV-masked min(D/Rx_k, 1.2). Each has a summed-area table, so the sums inside any usable square are O(1).
    /// </para>
    /// <para>
    /// Grid: pixel centres at <c>offset_u + (i + 0.5)·h</c> (likewise v); depths at <c>offset_n + j·DepthStep</c>.
    /// The pre-calculation model uses offset 0. Fine-tune models use the current array centre, so it lies exactly on the grid.
    /// Images are built lazily per orientation and cached, so manual edits re-use them.
    /// </para>
    /// </summary>
    public sealed class PlanningModel
    {
        private readonly Lazy<OrientationData>[] _data;

        public PlanningModel(IReadOnlyList<PlanningTarget> targets, DoseGrid dose,
            double pixelSize = 0.5, double depthStep = 0.5, Vec3 gridOffset = default)
        {
            if (targets.Count == 0) throw new ArgumentException("At least one PTV is required.", nameof(targets));
            if (targets.Count > 64) throw new ArgumentException("At most 64 PTVs are supported.", nameof(targets));
            Targets = targets; Dose = dose; PixelSize = pixelSize; DepthStep = depthStep; GridOffset = gridOffset;
            _data = new[]
            {
                new Lazy<OrientationData>(() => Build(ArrayOrientation.Deg0), LazyThreadSafetyMode.ExecutionAndPublication),
                new Lazy<OrientationData>(() => Build(ArrayOrientation.Deg270), LazyThreadSafetyMode.ExecutionAndPublication),
            };
        }

        public IReadOnlyList<PlanningTarget> Targets { get; }
        public DoseGrid Dose { get; }
        public double PixelSize { get; }
        public double DepthStep { get; }
        public Vec3 GridOffset { get; }

        public int TargetCount => Targets.Count;

        internal OrientationData Data(ArrayOrientation o) => _data[o == ArrayOrientation.Deg0 ? 0 : 1].Value;

        /// <summary>Build the images for an orientation now (otherwise built on first use).</summary>
        public void Prepare(ArrayOrientation o) => _ = Data(o);

        // ---------------------------------------------------------------------------------- grid helpers
        public double U0(ArrayOrientation o) => o.U(GridOffset);
        public double V0(ArrayOrientation o) => o.V(GridOffset);
        public double N0(ArrayOrientation o) => o.N(GridOffset);

        public double DepthOf(ArrayOrientation o, int depthIndex) => N0(o) + depthIndex * DepthStep;
        public int DepthIndexOf(ArrayOrientation o, double depth) => (int)Math.Round((depth - N0(o)) / DepthStep);

        /// <summary>Depth index range [first, last] in which PTV k has a non-empty cross-section.</summary>
        public (int First, int Last) DepthRange(int k, ArrayOrientation o)
        {
            var t = Data(o).Targets[k];
            return (t.FirstNonEmpty, t.LastNonEmpty);
        }

        /// <summary>Tight in-plane bounds (u, v) of PTV k.</summary>
        public (double UMin, double UMax, double VMin, double VMax) InPlaneBounds(int k, ArrayOrientation o)
        {
            var b = Targets[k].Mask.Bounds;
            return (o.U(b.Min), o.U(b.Max), b.Min.Z, b.Max.Z);
        }

        /// <summary>A_k,max: largest cross-section area of PTV k over all depths for orientation o (mm²).</summary>
        public double MaxArea(int k, ArrayOrientation o) => Data(o).Targets[k].MaxArea;

        // ---------------------------------------------------------------------------------- evaluation
        /// <summary>Area, high-dose area, mean ratio and score J for PTV k on the square of half-width <paramref name="half"/> centred at (cu, cv), depth index j.</summary>
        public PtvSample Sample(int k, ArrayOrientation o, int j, double cu, double cv, double half, double alpha)
        {
            var td = Data(o).Targets[k];
            var slice = td.Get(j);
            if (slice == null) return default;

            double h = PixelSize;
            double u0 = U0(o), v0 = V0(o);
            int i0 = FirstPixel(cu - half, u0, h) - slice.I0;
            int i1 = FirstPixel(cu + half, u0, h) - slice.I0;
            int l0 = FirstPixel(cv - half, v0, h) - slice.L0;
            int l1 = FirstPixel(cv + half, v0, h) - slice.L0;

            double count = slice.Mask.Sum(i0, i1, l0, l1);
            if (count <= 0) return new PtvSample(0, slice.Count * h * h, td.MaxArea, 0, 0, 0);
            double pix = h * h;
            double area = count * pix;
            double high = slice.High.Sum(i0, i1, l0, l1) * pix;
            double mean = slice.Ratio.Sum(i0, i1, l0, l1) / count;
            double j_ = Scoring.PtvScore(alpha, area, td.MaxArea, high, mean);
            return new PtvSample(area, slice.Count * pix, td.MaxArea, high, mean, j_);
        }

        /// <summary>Index of the first pixel whose centre is ≥ x (pixel centres at origin + (i + 0.5)·h).</summary>
        internal static int FirstPixel(double x, double origin, double h) =>
            (int)Math.Ceiling((x - origin) / h - 0.5 - 1e-9);

        /// <summary>N_k: diodes whose position lies inside PTV k for an array centred at patient point <paramref name="centre"/>.</summary>
        public int CountDiodesInside(int k, ArrayOrientation o, Vec3 centre)
        {
            var mask = Targets[k].Mask;
            double cu = o.U(centre), cv = o.V(centre), n = o.N(centre);
            if (!QuickOverlap(mask.Bounds, o, cu, cv, n)) return 0;
            int count = 0;
            foreach (var d in DiodeArray.Positions)
                if (mask.Contains(o.FromPlane(cu + d.U, cv + d.V, n))) count++;
            return count;
        }

        /// <summary>Diodes inside PTV k with dose ≥ Rx_k (dose from this model's grid).</summary>
        public int CountDiodesAtOrAboveRx(int k, ArrayOrientation o, Vec3 centre)
        {
            var t = Targets[k];
            double cu = o.U(centre), cv = o.V(centre), n = o.N(centre);
            if (!QuickOverlap(t.Mask.Bounds, o, cu, cv, n)) return 0;
            int count = 0;
            foreach (var d in DiodeArray.Positions)
            {
                var p = o.FromPlane(cu + d.U, cv + d.V, n);
                if (t.Mask.Contains(p) && Dose.Sample(p) >= t.RxGy) count++;
            }
            return count;
        }

        private static bool QuickOverlap(Box3 b, ArrayOrientation o, double cu, double cv, double n)
        {
            double h = DiodeArray.HalfExtent;
            return o.N(b.Min) <= n && n <= o.N(b.Max)
                && o.U(b.Min) <= cu + h && cu - h <= o.U(b.Max)
                && b.Min.Z <= cv + h && cv - h <= b.Max.Z;
        }

        /// <summary>
        /// Full metrics for every PTV on a plane (used for display, manual edits and final candidates).
        /// The depth is snapped to the nearest model depth (<see cref="DepthStep"/>) for the area/score terms;
        /// diode counts use the exact plane.
        /// </summary>
        public PlaneEvaluation Evaluate(ArrayPlane plane, OptimisationSettings s, CoverageReference? reference)
        {
            var o = plane.Orientation;
            int j = DepthIndexOf(o, plane.Depth);
            var list = new List<PtvPlaneMetrics>(TargetCount);
            for (int k = 0; k < TargetCount; k++)
            {
                var smp = Sample(k, o, j, plane.U, plane.V, s.UsableHalfWidth, s.Alpha);
                int n = smp.AreaInSquare > 0 ? CountDiodesInside(k, o, plane.Centre) : 0;
                int nRx = n > 0 ? CountDiodesAtOrAboveRx(k, o, plane.Centre) : 0;
                double best = reference?.BestScore[k] ?? 0;
                bool waived = reference?.DiodeRuleWaived[k] ?? false;
                list.Add(new PtvPlaneMetrics
                {
                    TargetIndex = k,
                    TargetId = Targets[k].Id,
                    RxGy = Targets[k].RxGy,
                    AreaInSquare = smp.AreaInSquare,
                    SliceArea = smp.SliceArea,
                    MaxArea = MaxArea(k, o),
                    HighDoseArea = smp.HighDoseArea,
                    MeanDoseRatio = smp.MeanRatio,
                    Score = smp.Score,
                    BestScore = best,
                    DiodesInPtv = n,
                    DiodesAtOrAboveRx = nRx,
                    Covered = reference != null && Scoring.IsCovered(smp.Score, best, s.Tau, n, s.MinDiodes, waived),
                    Clipped = smp.AreaInSquare > 0 && smp.ClippedFraction > s.ClipTolerance,
                });
            }
            return new PlaneEvaluation(plane, list);
        }

        /// <summary>Patient point for plane coordinates (u, v) at depth index j.</summary>
        public Vec3 PointAt(ArrayOrientation o, int j, double cu, double cv) => o.FromPlane(cu, cv, DepthOf(o, j));

        // ---------------------------------------------------------------------------------- building
        private OrientationData Build(ArrayOrientation o)
        {
            var result = new TargetData[TargetCount];
            for (int k = 0; k < TargetCount; k++) result[k] = BuildTarget(k, o);
            return new OrientationData(result);
        }

        private TargetData BuildTarget(int k, ArrayOrientation o)
        {
            var t = Targets[k];
            var b = t.Mask.Bounds;
            double h = PixelSize, u0 = U0(o), v0 = V0(o), n0 = N0(o);
            int iFirst = (int)Math.Floor((o.U(b.Min) - u0) / h - 0.5);
            int iLast = (int)Math.Ceiling((o.U(b.Max) - u0) / h - 0.5);
            int lFirst = (int)Math.Floor((b.Min.Z - v0) / h - 0.5);
            int lLast = (int)Math.Ceiling((b.Max.Z - v0) / h - 0.5);
            int jFirst = (int)Math.Floor((o.N(b.Min) - n0) / DepthStep);
            int jLast = (int)Math.Ceiling((o.N(b.Max) - n0) / DepthStep);
            int nu = iLast - iFirst + 1, nv = lLast - lFirst + 1, nd = jLast - jFirst + 1;

            var slices = new PtvSlice?[nd];
            double rx = t.RxGy;
            Parallel.For(0, nd, dj =>
            {
                double n = n0 + (jFirst + dj) * DepthStep;
                var mask = new double[nu * nv];
                var high = new double[nu * nv];
                var ratio = new double[nu * nv];
                int count = 0;
                for (int l = 0; l < nv; l++)
                {
                    double v = v0 + (lFirst + l + 0.5) * h;
                    for (int i = 0; i < nu; i++)
                    {
                        double u = u0 + (iFirst + i + 0.5) * h;
                        var p = o.FromPlane(u, v, n);
                        if (!t.Mask.Contains(p)) continue;
                        int idx = i + nu * l;
                        double d = Dose.Sample(p);
                        mask[idx] = 1;
                        high[idx] = d >= rx ? 1 : 0;
                        ratio[idx] = Math.Min(d / rx, Scoring.RatioCap);
                        count++;
                    }
                }
                if (count == 0) return;
                slices[dj] = new PtvSlice(iFirst, lFirst, count,
                    new SummedAreaTable(mask, nu, nv), new SummedAreaTable(high, nu, nv), new SummedAreaTable(ratio, nu, nv));
            });

            double maxArea = 0;
            int firstNonEmpty = int.MaxValue, lastNonEmpty = int.MinValue;
            for (int dj = 0; dj < nd; dj++)
            {
                var sl = slices[dj];
                if (sl == null) continue;
                maxArea = Math.Max(maxArea, sl.Count * h * h);
                firstNonEmpty = Math.Min(firstNonEmpty, jFirst + dj);
                lastNonEmpty = Math.Max(lastNonEmpty, jFirst + dj);
            }
            if (maxArea <= 0) { firstNonEmpty = jFirst; lastNonEmpty = jFirst - 1; }
            return new TargetData(jFirst, slices, maxArea, firstNonEmpty, lastNonEmpty);
        }

        internal sealed class OrientationData
        {
            public OrientationData(TargetData[] targets) { Targets = targets; }
            public TargetData[] Targets { get; }
        }

        internal sealed class TargetData
        {
            public TargetData(int jFirst, PtvSlice?[] slices, double maxArea, int firstNonEmpty, int lastNonEmpty)
            {
                JFirst = jFirst; Slices = slices; MaxArea = maxArea; FirstNonEmpty = firstNonEmpty; LastNonEmpty = lastNonEmpty;
            }

            public int JFirst { get; }
            public PtvSlice?[] Slices { get; }
            public double MaxArea { get; }
            public int FirstNonEmpty { get; }
            public int LastNonEmpty { get; }

            public PtvSlice? Get(int j)
            {
                int d = j - JFirst;
                return d < 0 || d >= Slices.Length ? null : Slices[d];
            }
        }

        internal sealed class PtvSlice
        {
            public PtvSlice(int i0, int l0, int count, SummedAreaTable mask, SummedAreaTable high, SummedAreaTable ratio)
            {
                I0 = i0; L0 = l0; Count = count; Mask = mask; High = high; Ratio = ratio;
            }

            /// <summary>Global pixel index of local pixel 0 (u and v).</summary>
            public int I0 { get; }
            public int L0 { get; }
            public int Count { get; }
            public SummedAreaTable Mask { get; }
            public SummedAreaTable High { get; }
            public SummedAreaTable Ratio { get; }
        }
    }
}
