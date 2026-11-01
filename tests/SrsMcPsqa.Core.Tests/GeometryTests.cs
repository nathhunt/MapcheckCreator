using System;
using System.Collections.Generic;
using System.Linq;
using SrsMcPsqa.Core.Detector;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Optimisation;
using Xunit;

namespace SrsMcPsqa.Core.Tests
{
    public class DetectorTests
    {
        [Fact]
        public void Has1013Diodes() => Assert.Equal(1013, DiodeArray.Count);

        [Fact]
        public void ExtentIs77mm()
        {
            Assert.Equal(-38.5, DiodeArray.Positions.Min(d => d.U), 9);
            Assert.Equal(38.5, DiodeArray.Positions.Max(d => d.U), 9);
            Assert.Equal(-38.5, DiodeArray.Positions.Min(d => d.V), 9);
            Assert.Equal(38.5, DiodeArray.Positions.Max(d => d.V), 9);
            Assert.Equal(77.0, DiodeArray.Extent, 9);
        }

        [Fact]
        public void NearestNeighbourSpacingIs2_47mm()
        {
            var p = DiodeArray.Positions;
            var centre = p.Single(d => d.U == 0 && d.V == 0);
            double nn = p.Where(d => !(d.U == 0 && d.V == 0)).Min(d => Math.Sqrt(d.U * d.U + d.V * d.V));
            Assert.Equal(2.47, nn, 2);
            Assert.Equal(DiodeArray.NearestNeighbourSpacing, nn, 9);
        }

        [Fact]
        public void LatticeIsSymmetricUnder90DegreeRotation()
        {
            var set = new HashSet<(double, double)>(DiodeArray.Positions.Select(d => (d.U, d.V)));
            Assert.All(DiodeArray.Positions, d => Assert.Contains((-d.V, d.U), set));
        }
    }

    public class IsoGeometryTests
    {
        private static readonly Vec3 O = new Vec3(1.2, 118.0, -32.5);
        private static readonly Vec3 Iclin = new Vec3(-12.3, 25.0, 40.1);

        [Fact]
        public void ArrayCentreMapsToUserOrigin()
        {
            var p = new Vec3(-5.0, 30.5, 52.0);
            var iso = IsoGeometry.QaIsocentre(O, Iclin, p);
            Assert.True(IsoGeometry.PatientToPhantom(p, Iclin, iso).ApproxEquals(O));
            Assert.True(IsoGeometry.ArrayCentreInPatient(O, Iclin, iso).ApproxEquals(p));
        }

        [Fact]
        public void ArrayAtClinicalIsoGivesIsoAtUserOrigin() =>
            Assert.True(IsoGeometry.QaIsocentre(O, Iclin, Iclin).ApproxEquals(O));

        [Fact]
        public void PatientPhantomRoundTrip()
        {
            var iso = new Vec3(3, 4, 5);
            var p = new Vec3(10, -20, 30);
            Assert.True(IsoGeometry.PhantomToPatient(IsoGeometry.PatientToPhantom(p, Iclin, iso), Iclin, iso).ApproxEquals(p));
        }

        [Fact]
        public void MovingArrayLeftMovesIsocentreRight()
        {
            var p = new Vec3(0, 30, 50);
            var iso = IsoGeometry.QaIsocentre(O, Iclin, p);
            var pNew = p + new Vec3(2, 0, 0); // array 2 mm towards patient left
            var shift = IsoGeometry.IsoShiftForArrayMove(p, pNew);
            Assert.True(shift.ApproxEquals(new Vec3(-2, 0, 0)));
            var isoNew = iso + shift;
            Assert.True(IsoGeometry.PatientToPhantom(pNew, Iclin, isoNew).ApproxEquals(O));
            Assert.True(isoNew.ApproxEquals(IsoGeometry.QaIsocentre(O, Iclin, pNew)));
        }

        [Theory]
        [InlineData(2, -1.5, 0, "Left 2.0 mm, Ant 1.5 mm, Sup 0.0 mm")]
        [InlineData(-3, 4, -0.25, "Right 3.0 mm, Post 4.0 mm, Inf 0.3 mm")]
        [InlineData(0.01, -0.01, 0, "Left 0.0 mm, Post 0.0 mm, Sup 0.0 mm")]
        public void ShiftLabels(double x, double y, double z, string expected) =>
            Assert.Equal(expected, Geometry.ShiftLabels.Describe(new Vec3(x, y, z)));

        [Theory]
        [InlineData(ArrayOrientation.Deg0)]
        [InlineData(ArrayOrientation.Deg270)]
        public void PlaneAxesRoundTrip(ArrayOrientation o)
        {
            var p = new Vec3(1, 2, 3);
            Assert.Equal(p, o.FromPlane(o.U(p), o.V(p), o.N(p)));
        }

        [Fact]
        public void PlaneAxesFollowTheBrief()
        {
            var p = new Vec3(1, 2, 3);
            Assert.Equal((1.0, 3.0, 2.0), (ArrayOrientation.Deg0.U(p), ArrayOrientation.Deg0.V(p), ArrayOrientation.Deg0.N(p)));
            Assert.Equal((2.0, 3.0, 1.0), (ArrayOrientation.Deg270.U(p), ArrayOrientation.Deg270.V(p), ArrayOrientation.Deg270.N(p)));
        }
    }

    public class GridTests
    {
        [Fact]
        public void TrilinearSamplingIsExactForLinearField()
        {
            var g = new GridGeometry(new Vec3(-10, -10, -10), new Vec3(1, 2, 2.5), 21, 11, 9);
            var dose = DoseGrid.FromFunction(g, p => 1 + 0.1 * p.X - 0.05 * p.Y + 0.02 * p.Z);
            var q = new Vec3(0.3, 1.7, -2.2);
            Assert.Equal(1 + 0.03 - 0.085 - 0.044, dose.Sample(q), 5);
            Assert.Equal(0, dose.Sample(new Vec3(100, 0, 0)));
        }

        [Fact]
        public void MaskVolumeOfSphere()
        {
            var t = Synthetic.Target(Synthetic.S("PTV", 0, 0, 0, 10));
            Assert.InRange(t.Mask.VolumeCc, 4.19 * 0.97, 4.19 * 1.03);
        }
    }

    public class RasterizerTests
    {
        private static IReadOnlyList<Point2> Circle(double r, int n = 128, double cx = 0, double cy = 0) =>
            Enumerable.Range(0, n).Select(i => new Point2(cx + r * Math.Cos(2 * Math.PI * i / n), cy + r * Math.Sin(2 * Math.PI * i / n))).ToList();

        private static IReadOnlyList<Point2> Square(double x0, double y0, double x1, double y1) =>
            new[] { new Point2(x0, y0), new Point2(x1, y0), new Point2(x1, y1), new Point2(x0, y1) };

        private static double SliceArea(StructureMask m, double z)
        {
            var g = m.Geometry;
            int k = (int)Math.Round((z - g.Origin.Z) / g.Spacing.Z);
            int count = 0;
            for (int j = 0; j < g.Ny; j++) for (int i = 0; i < g.Nx; i++) if (m.Inside[g.Index(i, j, k)]) count++;
            return count * g.Spacing.X * g.Spacing.Y;
        }

        [Fact]
        public void CircleArea()
        {
            var slices = new[] { new ContourSlice(0, new[] { Circle(10) }) };
            var m = ContourRasterizer.Rasterize(slices, 2.0);
            Assert.InRange(SliceArea(m, 0), Math.PI * 100 * 0.98, Math.PI * 100 * 1.02);
        }

        [Fact]
        public void HoleIsExcludedByEvenOdd()
        {
            var slices = new[] { new ContourSlice(4, new[] { Square(-10, -10, 10, 10), Square(-5, -5, 5, 5) }) };
            var m = ContourRasterizer.Rasterize(slices, 2.0);
            Assert.Equal(300, SliceArea(m, 4), 0);
            Assert.False(m.Contains(new Vec3(0, 0, 4)));
            Assert.True(m.Contains(new Vec3(7, 0, 4)));
        }

        [Fact]
        public void TwoIslandsAndSliceSpacing()
        {
            var slices = new[]
            {
                new ContourSlice(-2.5, new[] { Circle(3, 64, -10, 0), Circle(3, 64, 10, 0) }),
                new ContourSlice(0.0, new[] { Circle(3, 64, -10, 0), Circle(3, 64, 10, 0) }),
                new ContourSlice(2.5, new[] { Circle(3, 64, -10, 0) }),
            };
            var m = ContourRasterizer.Rasterize(slices, 2.5);
            Assert.Equal(2.5, m.Geometry.Spacing.Z);
            Assert.True(m.Contains(new Vec3(10, 0, 0)));
            Assert.False(m.Contains(new Vec3(10, 0, 2.5)));
            Assert.True(m.Contains(new Vec3(-10, 0, 2.5)));
            Assert.False(m.Contains(new Vec3(0, 0, 0)));
            Assert.InRange(SliceArea(m, 0), 2 * Math.PI * 9 * 0.95, 2 * Math.PI * 9 * 1.05);
        }
    }

    public class SummedAreaTableTests
    {
        [Fact]
        public void MatchesBruteForce()
        {
            var rnd = new Random(42);
            int w = 13, h = 9;
            var v = Enumerable.Range(0, w * h).Select(_ => rnd.NextDouble()).ToArray();
            var sat = new SummedAreaTable(v, w, h);
            for (int t = 0; t < 200; t++)
            {
                int i0 = rnd.Next(-3, w + 3), i1 = rnd.Next(-3, w + 3), l0 = rnd.Next(-3, h + 3), l1 = rnd.Next(-3, h + 3);
                double brute = 0;
                for (int l = Math.Max(0, l0); l < Math.Min(h, l1); l++)
                    for (int i = Math.Max(0, i0); i < Math.Min(w, i1); i++)
                        brute += v[i + w * l];
                Assert.Equal(brute, sat.Sum(i0, i1, l0, l1), 9);
            }
        }
    }

    public class ScoringTests
    {
        [Fact]
        public void PerfectPlaneScoresAboutOne() => Assert.Equal(1.0, Scoring.PtvScore(0.5, 100, 100, 100, 1.0), 9);

        [Fact]
        public void AlphaOneIgnoresArea() => Assert.Equal(0.5 * 0.2 + 0.5 * 0.9, Scoring.PtvScore(1.0, 50, 100, 20, 0.9), 9);

        [Fact]
        public void AlphaZeroIsAreaFraction() => Assert.Equal(0.5, Scoring.PtvScore(0.0, 50, 100, 20, 0.9), 9);

        [Fact]
        public void NoIntersectionScoresZero() => Assert.Equal(0, Scoring.PtvScore(0.5, 0, 100, 0, 0));

        [Fact]
        public void CoverageRule()
        {
            Assert.True(Scoring.IsCovered(0.86, 1.0, 0.85, 5, 5, false));
            Assert.False(Scoring.IsCovered(0.84, 1.0, 0.85, 50, 5, false));
            Assert.False(Scoring.IsCovered(0.95, 1.0, 0.85, 4, 5, false));
            Assert.True(Scoring.IsCovered(0.95, 1.0, 0.85, 4, 5, true));
        }

        [Fact]
        public void OrientationPenalties()
        {
            var mixed = new[] { ArrayOrientation.Deg0, ArrayOrientation.Deg270, ArrayOrientation.Deg270 };
            Assert.Equal(0, OrientationPreference.NoPreference.Penalty(mixed));
            Assert.Equal(1, OrientationPreference.PreferSame.Penalty(mixed));
            Assert.Equal(0, OrientationPreference.PreferSame.Penalty(new[] { ArrayOrientation.Deg0, ArrayOrientation.Deg0 }));
            Assert.Equal(2, OrientationPreference.Prefer0.Penalty(mixed));
            Assert.Equal(1, OrientationPreference.Prefer270.Penalty(mixed));
        }
    }
}
