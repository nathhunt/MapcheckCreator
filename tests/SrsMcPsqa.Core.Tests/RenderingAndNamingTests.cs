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
    }
}
