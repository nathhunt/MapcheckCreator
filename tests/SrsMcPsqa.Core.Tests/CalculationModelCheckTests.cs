using SrsMcPsqa.Core.Services;
using Xunit;

namespace SrsMcPsqa.Core.Tests
{
    public class CalculationModelCheckTests
    {
        [Fact] public void SameModelIsAllowed() => Assert.Null(CalculationModelCheck.BlockedReason("AcurosXB_1612", "AcurosXB_1612"));
        [Fact] public void CaseAndSpacesDontMatter() => Assert.Null(CalculationModelCheck.BlockedReason(" acurosxb_1612 ", "AcurosXB_1612"));

        [Fact]
        public void DifferentModelIsRefusedNamingBoth()
        {
            var why = CalculationModelCheck.BlockedReason("AcurosXB_1612", "AAA_1612");
            Assert.NotNull(why);
            Assert.Contains("AAA_1612", why);
            Assert.Contains("AcurosXB_1612", why);
        }

        [Fact] public void NotSetIsRefused() => Assert.Contains("isn't set", CalculationModelCheck.BlockedReason("", "AcurosXB_1612"));
        [Fact] public void UnknownClinicalModelIsCheckedOnTheQaPlanInstead()
        {
            Assert.Null(CalculationModelCheck.BlockedReason("AcurosXB_1612", ""));
            Assert.True(CalculationModelCheck.IsUnknown(""));
            Assert.Null(CalculationModelCheck.QaPlanBlockedReason("AcurosXB_1612", "acurosxb_1612", "MC1_0"));
            Assert.Contains("AAA_1612", CalculationModelCheck.QaPlanBlockedReason("AcurosXB_1612", "AAA_1612", "MC1_0"));
            Assert.Contains("didn't report", CalculationModelCheck.QaPlanBlockedReason("AcurosXB_1612", "", "MC1_0"));
            Assert.Contains("isn't set", CalculationModelCheck.QaPlanBlockedReason("", "AAA_1612", "MC1_0"));
        }
    }
}
