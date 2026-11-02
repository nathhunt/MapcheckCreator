using System.Linq;
using System.Threading.Tasks;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Optimisation;
using SrsMcPsqa.Core.Prescription;
using SrsMcPsqa.Core.Services;
using SrsMcPsqa.Fake;
using Xunit;
using Xunit.Abstractions;

namespace SrsMcPsqa.Core.Tests
{
    /// <summary>The whole workflow (minus the UI) against the fake service.</summary>
    public class FakeWorkflowTests
    {
        private readonly ITestOutputHelper _out;
        public FakeWorkflowTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public async Task RecommendCreateFineTuneMoveDelete()
        {
            var svc = new FakePlanningService { DelayScale = 0 };
            var patient = await svc.OpenPatientAsync("FAKE001");
            Assert.True(patient.CanModify);
            var plan = await svc.LoadClinicalPlanAsync("C1", "SRS_5Mets");
            Assert.Equal(6, plan.Ptvs.Count);
            Assert.True(PtvFilter.IsHelper(plan.Ptvs[5].Id, PtvFilter.DefaultHelperPatterns));
            Assert.Equal(RxSource.FromName, plan.Ptvs[0].RxSource);
            Assert.Equal(RxSource.PlanRx, plan.Ptvs[4].RxSource);

            var targets = plan.Ptvs.Where(p => !PtvFilter.IsHelper(p.Id, PtvFilter.DefaultHelperPatterns)).Select(p => new PlanningTarget(p.Id, p.RxGy, p.Mask!)).ToList();
            var model = new PlanningModel(targets, plan.Dose!);
            var settings = new OptimisationSettings();
            var analysis = new CandidateSearch(model, settings).Analyse();
            var rec = Recommender.Recommend(model, analysis);
            foreach (var p in rec.Planes) _out.WriteLine($"{p.Plane}: {string.Join(", ", p.CoveredTargets.Select(k => targets[k].Id))}");
            Assert.True(rec.AllCovered);

            var phantoms = await svc.EnsurePhantomImagesAsync(new TemplatePhantomSettings { PatientId = "TPL", Image0Id = "SMC_0", Image270Id = "SMC_270" },
                rec.Planes.Select(p => p.Plane.Orientation).Distinct().ToList());
            var existing = (await svc.ListQaPlansAsync("QA")).Select(p => p.PlanId).ToList();
            int n = 1;
            foreach (var p in rec.Planes)
            {
                var phantom = phantoms.Single(x => x.Orientation == p.Plane.Orientation);
                var id = QaPlanNaming.Generate(null, n++, p.Plane.Orientation, existing);
                existing.Add(id);
                var req = QaPlanRequests.Create(p.Plane, plan.Isocentre, phantom, "QA", id, p.CoveredTargets.Select(k => targets[k].Id));
                var result = await svc.CreateQaPlanAsync(req);
                Assert.True(result.CalculationSucceeded);

                // The array centre in the phantom (user origin) sees the planned patient point.
                Assert.True(IsoGeometry.PatientToPhantom(p.Plane.Centre, plan.Isocentre, result.Isocentre).ApproxEquals(phantom.UserOrigin, 1e-9));

                var assigned = p.CoveredTargets.Select(k => targets[k]).ToList();
                var proposal = FineTuneOptimizer.Propose(QaPlanRequests.FineTune(result, plan.Isocentre, phantom, assigned), settings);
                _out.WriteLine($"{id}: {proposal.ArrayMoveDescription}; objective {proposal.CurrentObjective:0.###} -> {proposal.PredictedObjective:0.###}");
                Assert.True(proposal.PredictedObjective >= proposal.CurrentObjective - 1e-9);

                var moved = await svc.MoveIsocentreAsync("QA", id, proposal.ProposedQaIsocentre, req.DoseRegion);
                Assert.Equal(proposal.ProposedQaIsocentre, moved.Isocentre);
            }

            var tagged = await svc.ListQaPlansAsync("QA");
            Assert.Equal(rec.Planes.Count, tagged.Count);
            Assert.All(tagged, t => Assert.NotNull(t.Tag));
            await svc.DeleteQaPlansAsync("QA", tagged.Select(t => t.PlanId).ToList());
            Assert.Empty(await svc.ListQaPlansAsync("QA"));
        }
    }
}
