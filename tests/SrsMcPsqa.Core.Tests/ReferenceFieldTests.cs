using System.Linq;
using System.Threading.Tasks;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Services;
using SrsMcPsqa.Fake;
using Xunit;

namespace SrsMcPsqa.Core.Tests
{
    public class ReferenceFieldTests
    {
        [Fact]
        public void GantryIsZeroForTheHorizontalArrayAndNinetyForTheVertical()
        {
            Assert.Equal(0, ReferenceField.GantryAngle(ArrayOrientation.Deg0));
            Assert.Equal(90, ReferenceField.GantryAngle(ArrayOrientation.Deg270));
        }

        [Fact]
        public void PlanIdsFollowThePatternAndGetALetterWhenTaken()
        {
            Assert.Equal("MC_5x5_0", ReferenceField.PlanId("MC_5x5_{orient}", ArrayOrientation.Deg0, new string[0]));
            Assert.Equal("MC_5x5_270", ReferenceField.PlanId(null, ArrayOrientation.Deg270, new string[0]));
            Assert.Equal("MC_5x5_0a", ReferenceField.PlanId(null, ArrayOrientation.Deg0, new[] { "mc_5x5_0" }));
            Assert.Equal("MC_5x5_0b", ReferenceField.PlanId(null, ArrayOrientation.Deg0, new[] { "MC_5x5_0", "MC_5x5_0a" }));
        }

        [Fact]
        public void OnePlanPerOrientationUsedInAFixedOrder()
        {
            var o = ReferenceField.Orientations(new[] { ArrayOrientation.Deg270, ArrayOrientation.Deg0, ArrayOrientation.Deg270 });
            Assert.Equal(new[] { ArrayOrientation.Deg0, ArrayOrientation.Deg270 }, o);
        }

        [Fact]
        public void BeamIdNamesTheEnergyWithinSixteenCharacters()
        {
            Assert.Equal("5x5 6X-FFF", ReferenceField.BeamId("6X-FFF"));
            Assert.True(ReferenceField.BeamId("10X-FFF-SOMETHING-LONG").Length <= 16);
        }

        [Fact]
        public void TagRoundTripsTheReferenceKind()
        {
            var tag = new QaPlanTag("1.2.3", ArrayOrientation.Deg270, new string[0], isReferenceField: true);
            Assert.True(QaPlanTag.TryParse(tag.Format(), out var parsed));
            Assert.True(parsed!.IsReferenceField);
            Assert.Equal(ArrayOrientation.Deg270, parsed.Orientation);
            // Plain QA plan tags (including ones written before this change) aren't reference plans.
            Assert.True(QaPlanTag.TryParse("SRSMC-PSQA|v1|src=1.2.3|orient=0|ptv=PTV1", out var qa));
            Assert.False(qa!.IsReferenceField);
        }

        [Fact]
        public async Task DemoCreatesAndFindsTheFiveByFiveAtTheOrigin()
        {
            var svc = new FakePlanningService { DelayScale = 0 };
            await svc.OpenPatientAsync("FAKE001");
            await svc.LoadClinicalPlanAsync("C1", "SRS_5Mets");
            var phantoms = await svc.EnsurePhantomImagesAsync(new TemplatePhantomSettings { PatientId = "TPL", Image0Id = "SMC_0", Image270Id = "SMC_270" },
                new[] { ArrayOrientation.Deg270 });
            var req = ReferenceField.Request(phantoms[0], "QA", "MC_5x5_270");
            Assert.True(req.IsReferenceField);
            Assert.True(req.Isocentre.ApproxEquals(phantoms[0].UserOrigin, 1e-9)); // no shift
            Assert.Empty(req.AssignedPtvIds);

            var result = await svc.CreateQaPlanAsync(req);
            Assert.True(result.IsReferenceField && result.CalculationSucceeded);
            Assert.InRange(result.Dose!.Sample(phantoms[0].UserOrigin), 0.95, 1.05);

            var found = Assert.Single(await svc.FindExistingQaPlansAsync());
            Assert.True(found.Result.IsReferenceField);
            Assert.Empty(found.AssignedPtvIds);
        }
    }
}
