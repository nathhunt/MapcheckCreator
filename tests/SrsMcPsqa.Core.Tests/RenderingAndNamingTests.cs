using System;
using System.Linq;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Rendering;
using Xunit;

namespace SrsMcPsqa.Core.Tests
{
    public class PlaneSamplingTests
    {
        [Fact]
        public void IsolineOfCircleHasCircumference()
        {
            var t = Synthetic.Target(Synthetic.S("A", 0, 0, 0, 10));
            var img = PlaneSampler.SampleMask(t.Mask, ArrayOrientation.Deg0, 0, 0, 0, 15, 0.25);
            var segs = PlaneSampler.Isolines(img, 0.5);
            double length = segs.Sum(s => s.Length);
            Assert.InRange(length, 2 * Math.PI * 10 * 0.95, 2 * Math.PI * 10 * 1.10);
            Assert.All(segs, s => Assert.InRange(Math.Sqrt(s.U1 * s.U1 + s.V1 * s.V1), 9.0, 11.0));
        }

        [Fact]
        public void DoseSampleLayout()
        {
            var g = new GridGeometry(new Vec3(-20, -20, -20), new Vec3(1, 1, 1), 41, 41, 41);
            var dose = DoseGrid.FromFunction(g, p => p.X + 2 * p.Z);
            var img = PlaneSampler.SampleDose(dose, ArrayOrientation.Deg0, 3, 1, 2, 5, 1);
            Assert.Equal(11, img.Width);
            Assert.Equal(-4, img.U0, 9);
            Assert.Equal(-4 + 2 * -3, img[0, 0], 4);     // u=-4, v=-3
            Assert.Equal(6 + 2 * 7, img[10, 10], 4);     // u=6, v=7
        }
    }

    public class NamingTests
    {
        [Fact]
        public void DefaultPattern()
        {
            Assert.Equal("MC1_0", QaPlanNaming.Generate(null, 1, ArrayOrientation.Deg0, new string[0]));
            Assert.Equal("SMC2_270", QaPlanNaming.Generate("SMC{n}_{orient}", 2, ArrayOrientation.Deg270, new string[0]));
        }

        [Fact]
        public void SkipsExistingIdsCaseInsensitively() =>
            Assert.Equal("MC3_0", QaPlanNaming.Generate(null, 1, ArrayOrientation.Deg0, new[] { "mc1_0", "MC2_0" }));

        [Fact]
        public void TruncatesTo13KeepingNumber()
        {
            var id = QaPlanNaming.Generate("VeryLongPrefix{n}_{orient}", 12, ArrayOrientation.Deg270, new string[0]);
            Assert.True(id.Length <= 13, id);
            Assert.Contains("12", id);
            Assert.EndsWith("_270", id);
        }

        // The plan number must be trimmed around where it was placed, not where its digits first occur from the end:
        // "7" and "2" also occur in "270", which used to cut the orientation ("PSQA_PLAN7_70") or its separator.
        [Theory]
        [InlineData("PSQA_PLAN{n}_{orient}", 7, ArrayOrientation.Deg270, "PSQA_PLA7_270")]
        [InlineData("PSQA_PLAN{n}_{orient}", 2, ArrayOrientation.Deg270, "PSQA_PLA2_270")]
        [InlineData("PSQA_PLAN{n}_{orient}", 12, ArrayOrientation.Deg270, "PSQA_PL12_270")]
        [InlineData("PSQA_PLAN{n}_{orient}", 1, ArrayOrientation.Deg0, "PSQA_PLAN1_0")]
        [InlineData("PSQA_{orient}_PLAN{n}", 7, ArrayOrientation.Deg270, "PSQA_270_PLA7")]
        public void TruncationTrimsTextBeforeTheNumberOnly(string pattern, int n, ArrayOrientation o, string expected) =>
            Assert.Equal(expected, QaPlanNaming.Generate(pattern, n, o, new string[0]));

        [Fact]
        public void PatternWithoutNumberAppendsOnCollision()
        {
            Assert.Equal("QA_0", QaPlanNaming.Generate("QA_{orient}", 1, ArrayOrientation.Deg0, new string[0]));
            Assert.Equal("QA_02", QaPlanNaming.Generate("QA_{orient}", 1, ArrayOrientation.Deg0, new[] { "QA_0" }));
        }

        [Fact]
        public void TagRoundTrip()
        {
            var tag = new QaPlanTag("1.2.3.4", ArrayOrientation.Deg270, new[] { "PTV1_24Gy", "PTV|2" });
            var text = tag.Format();
            Assert.StartsWith("SRSMC-PSQA|v1|src=1.2.3.4|orient=270|ptv=", text);
            Assert.True(QaPlanTag.TryParse("user note " + text, out var parsed));
            Assert.Equal("1.2.3.4", parsed!.SourcePlanUid);
            Assert.Equal(ArrayOrientation.Deg270, parsed.Orientation);
            Assert.Equal(new[] { "PTV1_24Gy", "PTV_2" }, parsed.PtvIds);
            Assert.False(QaPlanTag.TryParse("some other plan", out _));
            Assert.False(QaPlanTag.TryParse(null, out _));
        }
    }
}

namespace SrsMcPsqa.Core.Tests
{
    public class ShiftRoundingTests
    {
        [Fact]
        public void RoundsSmallComponentsToZero()
        {
            var v = Geometry.ShiftRounding.Snap(new Geometry.Vec3(0.6, -0.99, 1.0), 1.0);
            Assert.Equal(0, v.X); Assert.Equal(0, v.Y); Assert.Equal(1.0, v.Z); // exactly the threshold is kept
            Assert.Equal(0.3, Geometry.ShiftRounding.Snap(0.3, 0)); // 0 = off
        }

        [Fact]
        public void SnapsTheIsocentreShiftNotTheCentre()
        {
            var iso = new Geometry.Vec3(10, 20, 30);
            // Array centre 0.4 mm right, 12 mm ant, 0.7 mm inf of the clinical iso → shift (0.4, 12, 0.7) → (0, 12, 0).
            var c = Geometry.ShiftRounding.SnapArrayCentre(iso, new Geometry.Vec3(9.6, 8, 29.3), 1.0);
            Assert.True(c.ApproxEquals(new Geometry.Vec3(10, 8, 30), 1e-9));
        }

        [Theory]
        [InlineData(52.4, 1.0, 52.0)]
        [InlineData(52.5, 1.0, 53.0)]
        [InlineData(-17.3, 1.0, -17.0)]
        [InlineData(-17.5, 1.0, -18.0)]
        [InlineData(52.4, 0.5, 52.5)]
        [InlineData(52.2, 0.5, 52.0)]
        [InlineData(52.37, 0, 52.37)] // 0 = off
        public void RoundsToTheNearestStep(double value, double step, double expected) =>
            Assert.Equal(expected, Geometry.ShiftRounding.Round(value, step), 9);

        [Fact]
        public void ThresholdAppliesToTheRoundedShift()
        {
            // Threshold 2 mm, round to 1 mm: 1.4 → 1 → below 2 → 0; 1.6 → 2 → kept; 4.7 → 5.
            var v = Geometry.ShiftRounding.Snap(new Geometry.Vec3(1.4, 1.6, 4.7), 2.0, 1.0);
            Assert.Equal(0, v.X); Assert.Equal(2, v.Y); Assert.Equal(5, v.Z);
        }

        [Fact]
        public void ArrayCentreGivesAWholeMillimetreShift()
        {
            var iso = new Geometry.Vec3(10, 20, 30);
            // Shift = iso − centre = (52.4, 4.7, 18.5) → (52, 5, 19).
            var c = Geometry.ShiftRounding.SnapArrayCentre(iso, iso - new Geometry.Vec3(52.4, 4.7, 18.5), 2.0, 1.0);
            Assert.True((iso - c).ApproxEquals(new Geometry.Vec3(52, 5, 19), 1e-9));
        }

        [Fact]
        public void ArrowStepsNeverGetStuck()
        {
            // 0.5 mm step with 1 mm rounding: 52 → 52.5 would round back to 52 (or 53 one way only); always moves one step.
            var up = Geometry.ShiftRounding.Step(new Geometry.Vec3(52, 0, 0), new Geometry.Vec3(52.4, 0, 0), 2, 1);
            var down = Geometry.ShiftRounding.Step(new Geometry.Vec3(52, 0, 0), new Geometry.Vec3(51.6, 0, 0), 2, 1);
            Assert.Equal(53, up.X); Assert.Equal(51, down.X);
            // From 0 inside the 2 mm threshold: out to ±2.
            Assert.Equal(2, Geometry.ShiftRounding.Step(default, new Geometry.Vec3(0.5, 0, 0), 2, 1).X);
            Assert.Equal(-2, Geometry.ShiftRounding.Step(default, new Geometry.Vec3(-0.5, 0, 0), 2, 1).X);
            // Back from 2 towards 0: 1 is below the threshold → 0.
            Assert.Equal(0, Geometry.ShiftRounding.Step(new Geometry.Vec3(2, 0, 0), new Geometry.Vec3(1.5, 0, 0), 2, 1).X);
            // Threshold 2.5 with 1 mm rounding: out to 3 (the threshold rounded up to the step).
            Assert.Equal(3, Geometry.ShiftRounding.Step(default, new Geometry.Vec3(0.5, 0, 0), 2.5, 1).X);
            // A big step just snaps.
            Assert.Equal(57, Geometry.ShiftRounding.Step(new Geometry.Vec3(52, 0, 0), new Geometry.Vec3(57.2, 0, 0), 2, 1).X);
        }

        [Fact]
        public void FineTuneMoveLandsOnTheGridAndIgnoresSmallChanges()
        {
            // Current shift (52, −17, 19); wanted array move (3.4, 1.2, −2.6), threshold 2, round to 1.
            // X: shift 52 − 3.4 = 48.6 → 49 → move 3; Y: 1.2 < 2 → no move; Z: 19 + 2.6 = 21.6 → 22 → move −3.
            var m = Geometry.ShiftRounding.FineTuneMove(new Geometry.Vec3(52, -17, 19), new Geometry.Vec3(3.4, 1.2, -2.6), 2, 1);
            Assert.True(m.ApproxEquals(new Geometry.Vec3(3, 0, -3), 1e-9));
            // An off-grid shift (older plan) lands on the grid: 52.4 − 3 = 49.4 → 49 → move 3.4.
            Assert.Equal(3.4, Geometry.ShiftRounding.FineTuneMove(new Geometry.Vec3(52.4, 0, 0), new Geometry.Vec3(3, 0, 0), 2, 1).X, 9);
            // Rounding off: the move is unchanged.
            Assert.Equal(3.4, Geometry.ShiftRounding.FineTuneMove(new Geometry.Vec3(52, 0, 0), new Geometry.Vec3(3.4, 0, 0), 2, 0).X, 9);
        }
    }
}
