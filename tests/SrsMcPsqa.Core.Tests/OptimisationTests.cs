using System;
using System.Diagnostics;
using System.Linq;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Optimisation;
using Xunit;
using Xunit.Abstractions;
using static SrsMcPsqa.Core.Tests.Synthetic;

namespace SrsMcPsqa.Core.Tests
{
    public class PlanningModelTests
    {
        [Fact]
        public void CentralCrossSectionOfSphereInsideSquare()
        {
            var m = Model(S("A", 0, 0, 0, 10));
            var s = new OptimisationSettings();
            var smp = m.Sample(0, ArrayOrientation.Deg0, m.DepthIndexOf(ArrayOrientation.Deg0, 0), 0, 0, s.UsableHalfWidth, s.Alpha);
            Assert.InRange(smp.AreaInSquare, Math.PI * 100 * 0.95, Math.PI * 100 * 1.05);
            Assert.Equal(smp.AreaInSquare, smp.MaxArea, 6);
            Assert.Equal(smp.AreaInSquare, smp.HighDoseArea, 6);  // whole PTV at 110 %
            Assert.Equal(1.1, smp.MeanRatio, 3);
            Assert.Equal(0.5 * (0.5 + 0.55) + 0.5, smp.Score, 3);
            Assert.Equal(0, smp.ClippedFraction, 9);
        }

        [Fact]
        public void SquareEdgeClipsCrossSection()
        {
            var m = Model(S("A", 0, 0, 0, 10));
            var s = new OptimisationSettings();
            // Array centre 35.5 mm to the right: usable edge passes through the PTV centre, so half is clipped.
            var smp = m.Sample(0, ArrayOrientation.Deg0, m.DepthIndexOf(ArrayOrientation.Deg0, 0), 35.5, 0, s.UsableHalfWidth, s.Alpha);
            Assert.InRange(smp.ClippedFraction, 0.45, 0.55);
        }

        [Fact]
        public void DiodeCountMatchesAreaDensity()
        {
            var m = Model(S("A", 0, 0, 0, 10));
            int n = m.CountDiodesInside(0, ArrayOrientation.Deg0, Vec3.Zero);
            double density = 1013 / (77.0 * 77.0);
            Assert.InRange(n, Math.PI * 100 * density * 0.85, Math.PI * 100 * density * 1.15);
            Assert.Equal(n, m.CountDiodesAtOrAboveRx(0, ArrayOrientation.Deg0, Vec3.Zero));
        }

        [Fact]
        public void MixedPrescriptionsUseOwnRx()
        {
            var a = S("A_20Gy", -15, 0, 0, 6, 20);
            var b = S("B_30Gy", 15, 0, 0, 6, 30);
            var s = new OptimisationSettings();
            var model = Model(a, b);
            var e = model.Evaluate(new ArrayPlane(ArrayOrientation.Deg0, Vec3.Zero), s, null);
            Assert.All(e.Targets, t => Assert.InRange(t.HighDoseFraction, 0.97, 1.0));

            // Same dose, but B's Rx raised to 40 Gy (dose is 33 Gy): no high-dose area for B.
            var model2 = new PlanningModel(new[] { Target(a), Target(b with { RxGy = 40 }) }, Dose(new[] { a, b }));
            var e2 = model2.Evaluate(new ArrayPlane(ArrayOrientation.Deg0, Vec3.Zero), s, null);
            Assert.InRange(e2.Targets[0].HighDoseFraction, 0.97, 1.0);
            Assert.Equal(0, e2.Targets[1].HighDoseArea);
            Assert.Equal(0, e2.Targets[1].DiodesAtOrAboveRx);
            Assert.InRange(e2.Targets[1].MeanDoseRatio, 0.82, 0.83);
        }
    }

    public class RecommendationTests
    {
        private readonly ITestOutputHelper _out;
        public RecommendationTests(ITestOutputHelper output) { _out = output; }

        private Recommendation Run(OptimisationSettings s, params Sphere[] spheres)
        {
            var sw = Stopwatch.StartNew();
            var rec = Recommender.Recommend(Model(spheres), s);
            _out.WriteLine($"{spheres.Length} PTVs -> {rec.Planes.Count} plan(s) in {sw.ElapsedMilliseconds} ms");
            foreach (var p in rec.Planes)
                _out.WriteLine($"  {p.Plane}  covered: {string.Join(", ", p.CoveredTargets)}  score {p.Score:0.###}");
            foreach (var w in rec.Warnings) _out.WriteLine("  ! " + w);
            return rec;
        }

        [Fact]
        public void SinglePtv()
        {
            var rec = Run(new OptimisationSettings(), S("A", 5, -3, 10, 8));
            var plane = Assert.Single(rec.Planes);
            Assert.InRange(plane.Plane.Depth - plane.Plane.Orientation.N(new Vec3(5, -3, 10)), -0.51, 0.51);
            Assert.Equal(CoverageStatus.Covered, rec.Targets[0].Status);
            Assert.False(plane.Evaluation.Targets[0].Clipped);
            Assert.True(plane.Evaluation.Targets[0].DiodesInPtv >= 5);
            // Equal-score positions are resolved by centring the PTV in the array.
            Assert.InRange(plane.Plane.U - plane.Plane.Orientation.U(new Vec3(5, -3, 10)), -0.51, 0.51);
            Assert.InRange(plane.Plane.V - 10, -0.51, 0.51);
        }

        [Fact]
        public void ThreePtvsFitOneArray()
        {
            var rec = Run(new OptimisationSettings(),
                S("A", -20, 0, 0, 6), S("B", 20, 0, 15, 6), S("C", 0, 0, -20, 6));
            var plane = Assert.Single(rec.Planes);
            Assert.Equal(ArrayOrientation.Deg0, plane.Plane.Orientation); // only 0° (normal AP) passes through all three
            Assert.Equal(3, plane.CoveredTargets.Count);
            Assert.All(rec.Targets, t => Assert.Equal(CoverageStatus.Covered, t.Status));
        }

        [Fact]
        public void PtvsTooFarApartNeedTwoPlans()
        {
            var rec = Run(new OptimisationSettings(), S("A", -50, 0, 0, 6), S("B", 50, 0, 0, 6));
            Assert.Equal(2, rec.Planes.Count);
            Assert.True(rec.AllCovered);
            Assert.All(rec.Targets, t => Assert.NotNull(t.PrimaryPlane));
            Assert.NotEqual(rec.Targets[0].PrimaryPlane, rec.Targets[1].PrimaryPlane);
            Assert.Contains(rec.Planes, p => p.Plane.Centre.ApproxEquals(new Vec3(-50, 0, 0), 0.51));
            Assert.Contains(rec.Planes, p => p.Plane.Centre.ApproxEquals(new Vec3(50, 0, 0), 0.51));
        }

        [Fact]
        public void PtvLargerThanArrayIsPartial()
        {
            var rec = Run(new OptimisationSettings(), S("Big", 0, 0, 0, 45));
            Assert.Single(rec.Planes);
            Assert.Equal(CoverageStatus.Partial, rec.Targets[0].Status);
            Assert.Contains(rec.Warnings, w => w.Contains("partial"));
        }

        [Fact]
        public void TinyPtvWaivesDiodeRule()
        {
            var rec = Run(new OptimisationSettings { MinDiodes = 50 }, S("Tiny", 0, 0, 0, 2));
            Assert.Single(rec.Planes);
            Assert.Equal(CoverageStatus.LowDiodeCount, rec.Targets[0].Status);
        }

        [Fact]
        public void PairAlongXPrefersZeroDegrees()
        {
            var pair = new[] { S("A", -20, 0, 0, 6), S("B", 20, 0, 0, 6) };
            var free = Run(new OptimisationSettings(), pair);
            Assert.Equal(ArrayOrientation.Deg0, Assert.Single(free.Planes).Plane.Orientation);

            // Fewer plans beats orientation preference.
            var prefer270 = Run(new OptimisationSettings { OrientationPreference = OrientationPreference.Prefer270 }, pair);
            Assert.Equal(ArrayOrientation.Deg0, Assert.Single(prefer270.Planes).Plane.Orientation);

            // A hard constraint forces two 270° plans.
            var only270 = Run(new OptimisationSettings { OrientationPreference = OrientationPreference.Only270 }, pair);
            Assert.Equal(2, only270.Planes.Count);
            Assert.All(only270.Planes, p => Assert.Equal(ArrayOrientation.Deg270, p.Plane.Orientation));
            Assert.True(only270.AllCovered);
        }

        [Theory]
        [InlineData(OrientationPreference.Prefer0, ArrayOrientation.Deg0)]
        [InlineData(OrientationPreference.Prefer270, ArrayOrientation.Deg270)]
        [InlineData(OrientationPreference.Only0, ArrayOrientation.Deg0)]
        [InlineData(OrientationPreference.Only270, ArrayOrientation.Deg270)]
        public void PreferenceDecidesForSymmetricPtv(OrientationPreference pref, ArrayOrientation expected)
        {
            var rec = Run(new OptimisationSettings { OrientationPreference = pref }, S("A", 0, 0, 0, 8));
            Assert.Equal(expected, Assert.Single(rec.Planes).Plane.Orientation);
        }

        [Fact]
        public void TenPtvsPerformance()
        {
            var spheres = Enumerable.Range(0, 10).Select(i =>
                S($"PTV{i + 1}", -45 + 10 * i, (i % 3 - 1) * 25, ((i * 7) % 5 - 2) * 18, 4 + i % 4 * 2)).ToArray();
            var sw = Stopwatch.StartNew();
            var rec = Run(new OptimisationSettings(), spheres);
            Assert.True(rec.AllCovered);
            Assert.InRange(rec.Planes.Count, 2, 10);
            Assert.True(sw.Elapsed.TotalSeconds < 60, $"took {sw.Elapsed.TotalSeconds:0.0} s");
        }
    }

    public class SetCoverSolverTests
    {
        private const ArrayOrientation D0 = ArrayOrientation.Deg0;
        private const ArrayOrientation D270 = ArrayOrientation.Deg270;

        [Fact]
        public void FindsMinimumNumberOfSets()
        {
            var opts = new[]
            {
                Candidate(D0, 0b011, 1, 1, 0),
                Candidate(D0, 0b100, 0, 0, 1),
                Candidate(D0, 0b110, 0, 1, 1),
                Candidate(D0, 0b001, 1, 0, 0),
            };
            var sol = SetCoverSolver.Solve(opts, 0b111, OrientationPreference.NoPreference);
            Assert.True(sol.Complete);
            Assert.Equal(2, sol.Chosen.Count);
        }

        [Fact]
        public void PreferSameOrientation()
        {
            var opts = new[]
            {
                Candidate(D0, 0b01, 1.0, 0),
                Candidate(D270, 0b10, 0, 1.0),
                Candidate(D0, 0b10, 0, 0.9),
                Candidate(D270, 0b01, 0.9, 0),
            };
            var none = SetCoverSolver.Solve(opts, 0b11, OrientationPreference.NoPreference);
            Assert.Equal(2.0, none.Value, 9); // best value mixes orientations

            var same = SetCoverSolver.Solve(opts, 0b11, OrientationPreference.PreferSame);
            Assert.Single(same.Chosen.Select(c => c.Orientation).Distinct());
            Assert.Equal(0, same.Penalty);
        }

        [Fact]
        public void PlanCountComesBeforePreference()
        {
            var opts = new[]
            {
                Candidate(D0, 0b11, 1, 1),
                Candidate(D270, 0b01, 1, 0),
                Candidate(D270, 0b10, 0, 1),
            };
            var sol = SetCoverSolver.Solve(opts, 0b11, OrientationPreference.Prefer270);
            Assert.Equal(D0, Assert.Single(sol.Chosen).Orientation);
        }

        [Fact]
        public void SlotLimitFallsBackToMaxCoverage()
        {
            var opts = new[]
            {
                Candidate(D0, 0b001, 1, 0, 0),
                Candidate(D0, 0b110, 0, 1, 1),
            };
            var sol = SetCoverSolver.Solve(opts, 0b111, OrientationPreference.NoPreference, slots: new SlotConstraint(1, 0, 0));
            Assert.False(sol.Complete);
            Assert.Equal(0b110UL, sol.Covered);
        }

        [Fact]
        public void SlotOrientationsAreHonoured()
        {
            var opts = new[]
            {
                Candidate(D0, 0b11, 1, 1),
                Candidate(D270, 0b01, 1, 0),
                Candidate(D270, 0b10, 0, 1),
            };
            var sol = SetCoverSolver.Solve(opts, 0b11, OrientationPreference.NoPreference, slots: new SlotConstraint(2, 0, 2));
            Assert.True(sol.Complete);
            Assert.All(sol.Chosen, c => Assert.Equal(D270, c.Orientation));
        }
    }

    public class ConstrainedOptimizerTests
    {
        private static readonly Sphere[] Three = { S("A", -20, 0, 0, 6), S("B", 20, 0, 15, 6), S("C", 0, 0, -20, 6) };

        [Fact]
        public void ForcingTwoPlansStillCoversAll()
        {
            var model = Model(Three);
            var analysis = new CandidateSearch(model, new OptimisationSettings()).Analyse();
            var rec = ConstrainedOptimizer.Optimise(model, analysis, new[] { new PlaneConstraint(), new PlaneConstraint() });
            Assert.Equal(2, rec.Planes.Count);
            Assert.True(rec.AllCovered);
        }

        [Fact]
        public void ImpossibleOrientationIsFlagged()
        {
            var pair = new[] { S("A", -20, 0, 0, 6), S("B", 20, 0, 0, 6) };
            var model = Model(pair);
            var analysis = new CandidateSearch(model, new OptimisationSettings()).Analyse();
            var rec = ConstrainedOptimizer.Optimise(model, analysis, new[] { new PlaneConstraint(ArrayOrientation.Deg270) });
            Assert.Single(rec.Planes);
            Assert.Equal(ArrayOrientation.Deg270, rec.Planes[0].Plane.Orientation);
            Assert.Contains(rec.Targets, t => t.Status == CoverageStatus.NotCovered);
        }

        [Fact]
        public void RequiredAssignmentIsMet()
        {
            var model = Model(Three);
            var analysis = new CandidateSearch(model, new OptimisationSettings()).Analyse();
            var rec = ConstrainedOptimizer.Optimise(model, analysis, new[]
            {
                new PlaneConstraint(ArrayOrientation.Deg0, new[] { 0, 1 }),
                new PlaneConstraint(ArrayOrientation.Deg270),
            });
            Assert.Equal(2, rec.Planes.Count);
            Assert.Equal(ArrayOrientation.Deg0, rec.Planes[0].Plane.Orientation);
            Assert.Contains(0, rec.Planes[0].CoveredTargets);
            Assert.Contains(1, rec.Planes[0].CoveredTargets);
            Assert.Equal(ArrayOrientation.Deg270, rec.Planes[1].Plane.Orientation);
            Assert.True(rec.AllCovered);
        }

        [Fact]
        public void InfeasibleAssignmentMaximisesMinimumAndWarns()
        {
            var far = new[] { S("A", -50, 0, 0, 6), S("B", 50, 0, 0, 6) };
            var model = Model(far);
            var analysis = new CandidateSearch(model, new OptimisationSettings()).Analyse();
            var rec = ConstrainedOptimizer.Optimise(model, analysis, new[] { new PlaneConstraint(null, new[] { 0, 1 }) });
            Assert.Single(rec.Planes);
            Assert.Contains(rec.Warnings, w => w.Contains("cannot cover"));
        }
    }

    public class FineTuneTests
    {
        private static readonly Vec3 O = new Vec3(1.0, 120.0, -30.0);
        private static readonly Vec3 Iclin = new Vec3(3, -5, 2);
        private static readonly Sphere Ptv = S("A", 0, 0, 0, 8, 20);

        private static FineTuneProposal Run(Vec3 arrayCentre)
        {
            var iso = IsoGeometry.QaIsocentre(O, Iclin, arrayCentre);
            // "Calculated" QA dose in phantom coordinates: the patient dose moved by (I_ph − I_clin).
            var qaDose = Dose(new[] { Ptv }).Translate(iso - Iclin);
            return FineTuneOptimizer.Propose(new FineTuneRequest
            {
                Orientation = ArrayOrientation.Deg0,
                PhantomUserOrigin = O,
                ClinicalIsocentre = Iclin,
                CurrentQaIsocentre = iso,
                QaDose = qaDose,
                Targets = new[] { Target(Ptv) },
            }, new OptimisationSettings());
        }

        [Fact]
        public void MovesClippedOffDepthArrayBackOntoPtv()
        {
            var start = new Vec3(30, 3, 0); // 30 mm off in-plane (clipped), 3 mm off in depth
            var p = Run(start);
            Assert.True(p.Current.Targets[0].Clipped);
            Assert.True(p.Improves);
            Assert.False(p.Predicted.Targets[0].Clipped);
            Assert.InRange(p.ProposedPlane.Depth, -0.5, 0.5);
            Assert.True(p.ProposedQaIsocentre.ApproxEquals(IsoGeometry.QaIsocentre(O, Iclin, p.ProposedPlane.Centre)));
            Assert.True(p.IsoShift.ApproxEquals(-p.ArrayMoveRelativeToPatient));
        }

        [Fact]
        public void NoChangeWhenAlreadyOptimal()
        {
            var p = Run(Vec3.Zero);
            Assert.True(p.IsoShift.Length < 1e-9, p.IsoShift.ToString());
            Assert.False(p.Improves);
        }
    }
}
