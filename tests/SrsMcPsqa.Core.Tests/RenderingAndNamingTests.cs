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
            Assert.Equal("SMC1_0", QaPlanNaming.Generate(null, 1, ArrayOrientation.Deg0, new string[0]));
            Assert.Equal("SMC2_270", QaPlanNaming.Generate("SMC{n}_{orient}", 2, ArrayOrientation.Deg270, new string[0]));
        }

        [Fact]
        public void SkipsExistingIdsCaseInsensitively() =>
            Assert.Equal("SMC3_0", QaPlanNaming.Generate(null, 1, ArrayOrientation.Deg0, new[] { "smc1_0", "SMC2_0" }));

        [Fact]
        public void TruncatesTo13KeepingNumber()
        {
            var id = QaPlanNaming.Generate("VeryLongPrefix{n}_{orient}", 12, ArrayOrientation.Deg270, new string[0]);
            Assert.True(id.Length <= 13, id);
            Assert.Contains("12", id);
            Assert.EndsWith("_270", id);
        }

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
