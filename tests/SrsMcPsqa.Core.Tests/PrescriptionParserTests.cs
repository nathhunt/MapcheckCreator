using System.Collections.Generic;
using SrsMcPsqa.Core.Prescription;
using Xunit;

namespace SrsMcPsqa.Core.Tests
{
    public class PrescriptionParserTests
    {
        [Theory]
        [InlineData("PTV_24Gy", 24)]
        [InlineData("ptv_24gy", 24)]
        [InlineData("PTV1 20Gy/1#", 20)]
        [InlineData("PTV 30Gy 5#", 30)]
        [InlineData("PTV 18 Gy", 18)]
        [InlineData("PTV_21_Gy", 21)]
        [InlineData("PTV_16.5Gy", 16.5)]
        [InlineData("PTV_16,5Gy", 16.5)]
        [InlineData("PTV_L_Front_2700cGy_3fx", 27)]
        [InlineData("PTV_R_Occ_2000cGy", 20)]
        [InlineData("PTV 2100 cGy", 21)]
        [InlineData("PTV2400", 24)]
        [InlineData("PTV_2400", 24)]
        [InlineData("PTV1_2700_3fx", 27)]
        [InlineData("PTV2_3000_5#", 30)]
        [InlineData("PTV_3x9Gy", 27)]
        [InlineData("PTV_5x6Gy", 30)]
        [InlineData("PTV_24G", 24)]
        [InlineData("PTV3_18G_1fx", 18)]
        [InlineData("PTV_Cerebellum_24Gy_3F", 24)]
        public void Parses(string id, double expectedGy)
        {
            Assert.True(PrescriptionParser.TryParse(id, out var gy), id);
            Assert.Equal(expectedGy, gy, 6);
        }

        [Theory]
        [InlineData("PTV_eval")]
        [InlineData("PTV")]
        [InlineData("PTV1")]
        [InlineData("PTV_10")]
        [InlineData("PTV_3fx")]
        [InlineData("PTV_x3")]
        [InlineData("PTV2_5#")]
        [InlineData("zPTV_opt")]
        [InlineData("PTV_5000Gy")]
        [InlineData("")]
        [InlineData(null)]
        public void DoesNotParse(string? id) => Assert.False(PrescriptionParser.TryParse(id, out _), id ?? "null");

        [Fact]
        public void ResolverPrefersNameThenPrescriptionThenPlan()
        {
            var targets = new Dictionary<string, double> { ["PTV_eval"] = 18.0 };
            Assert.Equal((24.0, RxSource.FromName), PrescriptionResolver.Resolve("PTV_24Gy", targets, 20));
            Assert.Equal((18.0, RxSource.FromPrescription), PrescriptionResolver.Resolve("ptv_EVAL", targets, 20));
            Assert.Equal((20.0, RxSource.PlanRx), PrescriptionResolver.Resolve("PTV_other", targets, 20));
            Assert.Equal((0.0, RxSource.None), PrescriptionResolver.Resolve("PTV_other", null, null));
        }
    }
}
