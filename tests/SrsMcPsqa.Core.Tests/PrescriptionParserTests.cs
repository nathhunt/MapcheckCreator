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
        [InlineData("PTV_24G", 24)]
        [InlineData("PTV3_18G_1fx", 18)]
        [InlineData("PTV_Cerebellum_24Gy_3F", 24)]
        [InlineData("PTV30", 30)]
        [InlineData("PTV25", 25)]
        [InlineData("PTV25_Eval", 25)]
        [InlineData("zPTV25", 25)]
        [InlineData("ZPTV30", 30)]
        [InlineData("PTV18_3fx", 18)]
        public void Parses(string id, double expectedGy)
        {
            Assert.True(PrescriptionParser.TryParse(id, out var gy), id);
            Assert.Equal(expectedGy, gy, 6);
        }

        [Theory]
        [InlineData("PTV_eval")]
        [InlineData("PTV")]
        [InlineData("PTV1")]
        [InlineData("PTV2_eval")]
        [InlineData("zPTVin")]
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

namespace SrsMcPsqa.Core.Tests
{
    public class PtvFilterTests
    {
        [Xunit.Theory]
        [Xunit.InlineData("zPTV25", true)]
        [Xunit.InlineData("ZPTV30", true)]
        [Xunit.InlineData("zPTVin", true)]
        [Xunit.InlineData("PTV30", false)]
        [Xunit.InlineData("PTV25_Eval", true)]
        [Xunit.InlineData("PTV_eval2", true)]
        [Xunit.InlineData("PTV25", false)]
        public void DefaultHelpersAreZAndEval(string id, bool helper) =>
            Xunit.Assert.Equal(helper, SrsMcPsqa.Core.Prescription.PtvFilter.IsHelper(id, SrsMcPsqa.Core.Prescription.PtvFilter.DefaultHelperPatterns));

        [Xunit.Fact]
        public void BarePatternIsPrefix()
        {
            Xunit.Assert.True(SrsMcPsqa.Core.Prescription.PtvFilter.IsHelper("zPTV", "z"));
            Xunit.Assert.False(SrsMcPsqa.Core.Prescription.PtvFilter.IsHelper("PTVz", "z"));
            Xunit.Assert.True(SrsMcPsqa.Core.Prescription.PtvFilter.IsHelper("PTV_opt", "*_opt"));
        }

        [Xunit.Fact]
        public void EmptyPrefixListMeansNoHelpers() =>
            Xunit.Assert.False(SrsMcPsqa.Core.Prescription.PtvFilter.IsHelper("zPTV", ""));
    }
}
