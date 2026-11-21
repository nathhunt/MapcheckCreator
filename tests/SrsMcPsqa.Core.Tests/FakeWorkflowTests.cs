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

        /// <summary>Synchronous IProgress so events are recorded in order (Progress&lt;T&gt; posts asynchronously).</summary>
        private sealed class Recorder : System.IProgress<QaProgressEvent>
        {
            public readonly System.Collections.Generic.List<QaProgressEvent> Events = new System.Collections.Generic.List<QaProgressEvent>();
            public void Report(QaProgressEvent value) { lock (Events) Events.Add(value); }
        }

        [Fact]
        public async Task CreateReportsStepsCalculationAndMus()
        {
            var svc = new FakePlanningService { DelayScale = 0 };
            await svc.OpenPatientAsync("FAKE001");
            var plan = await svc.LoadClinicalPlanAsync("C1", "SRS_5Mets");
            var rec = new Recorder();
            var phantoms = await svc.EnsurePhantomImagesAsync(new TemplatePhantomSettings { PatientId = "TPL", Image0Id = "SMC_0", Image270Id = "SMC_270" },
                new[] { ArrayOrientation.Deg0 }, steps: rec);
            Assert.Contains(rec.Events, e => e.PlanId == QaSteps.PhantomImages && e.State == QaStepState.Done);

            var plane = new ArrayPlane(ArrayOrientation.Deg0, plan.Isocentre);
            var req = QaPlanRequests.Create(plane, plan.Isocentre, phantoms[0], "QA", "SMC1_0", new[] { plan.Ptvs[0].Id });
            rec.Events.Clear();
            await svc.CreateQaPlanAsync(req, steps: rec);

            var ev = rec.Events;
            Assert.All(ev, e => Assert.Equal("SMC1_0", e.PlanId));
            // Every step that starts also ends, in order, and nothing fails.
            foreach (var started in ev.Where(e => e.State == QaStepState.Running))
                Assert.Contains(ev.Skip(ev.IndexOf(started) + 1), e => e.Step == started.Step && e.State == QaStepState.Done && e.Duration.HasValue);
            Assert.DoesNotContain(ev, e => e.State == QaStepState.Failed);

            var calc = ev.Single(e => e.IsCalculation && e.State == QaStepState.Running && e.EclipsePercent == null && e.EclipseMessage == null);
            Assert.True(calc.ControlPoints > 0 && calc.Beams > 0 && !string.IsNullOrEmpty(calc.Model));
            Assert.True(ev.IndexOf(calc) > ev.FindIndex(e => e.Step == "Copy beams" && e.State == QaStepState.Done));
            var mu = ev.Single(e => e.MuResults != null);
            Assert.Equal(calc.Beams, mu.MuResults!.Count);
            Assert.All(mu.MuResults, r => Assert.True(r.Matches));
        }

        [Fact]
        public async Task FindsQaPlansCreatedEarlierForThisPlan()
        {
            var svc = new FakePlanningService { DelayScale = 0 };
            await svc.OpenPatientAsync("FAKE001");
            var plan = await svc.LoadClinicalPlanAsync("C1", "SRS_5Mets");
            Assert.Empty(await svc.FindExistingQaPlansAsync());
            var phantoms = await svc.EnsurePhantomImagesAsync(new TemplatePhantomSettings { PatientId = "TPL", Image0Id = "SMC_0", Image270Id = "SMC_270" },
                new[] { ArrayOrientation.Deg0 });
            var req = QaPlanRequests.Create(new ArrayPlane(ArrayOrientation.Deg0, plan.Isocentre), plan.Isocentre, phantoms[0], "QA", "MC1_0", new[] { plan.Ptvs[0].Id });
            var created = await svc.CreateQaPlanAsync(req);
            var found = Assert.Single(await svc.FindExistingQaPlansAsync());
            Assert.Equal("MC1_0", found.Result.PlanId);
            Assert.Equal(ArrayOrientation.Deg0, found.Result.Orientation);
            Assert.True(found.Result.Isocentre.ApproxEquals(created.Isocentre, 1e-9));
            Assert.NotNull(found.Result.Dose);
            Assert.Equal(new[] { plan.Ptvs[0].Id }, found.AssignedPtvIds);
        }

        [Fact]
        public async Task EnsureCourseCreatesMissingCourseOnce()
        {
            var svc = new FakePlanningService { DelayScale = 0 };
            await svc.OpenPatientAsync("FAKE001");
            var c = await svc.EnsureCourseAsync("QA2");
            Assert.Equal("QA2", c.Id);
            Assert.Empty(await svc.ListQaPlansAsync("QA2"));
            Assert.Equal("QA2", (await svc.EnsureCourseAsync("QA2")).Id); // existing: returned, not duplicated
        }

        [Fact]
        public void QaStepsRunReportsFailure()
        {
            var rec = new Recorder();
            Assert.Throws<System.InvalidOperationException>(() =>
                QaSteps.Run<int>(rec, "P", "Boom", () => throw new System.InvalidOperationException("bad")));
            Assert.Equal(new[] { QaStepState.Running, QaStepState.Failed }, rec.Events.Select(e => e.State));
            Assert.Equal("bad", rec.Events[1].Detail);
        }

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

        [Fact]
        public async Task DeleteRefusesAllWhenOnePlanIsUntagged()
        {
            var svc = new FakePlanningService { DelayScale = 0 };
            await svc.OpenPatientAsync("FAKE001");
            await svc.LoadClinicalPlanAsync("C1", "SRS_5Mets");
            var ph = (await svc.EnsurePhantomImagesAsync(new TemplatePhantomSettings { PatientId = "TPL", Image0Id = "SMC_0", Image270Id = "SMC_270" },
                new[] { ArrayOrientation.Deg0 })).Single();
            await svc.CreateQaPlanAsync(new QaPlanRequest
            {
                QaCourseId = "QA", PlanId = "MC1_0", Orientation = ArrayOrientation.Deg0, StructureSetUid = ph.StructureSetUid,
                Isocentre = ph.UserOrigin, DoseRegion = QaPlanRequests.DoseRegion(ArrayOrientation.Deg0, ph.UserOrigin),
            });
            svc.AddUntaggedPlanForTests("QA", "USER_PLAN");

            // The tagged plan comes first: it must not be deleted when the untagged one is refused.
            var ex = await Assert.ThrowsAsync<System.InvalidOperationException>(() => svc.DeleteQaPlansAsync("QA", new[] { "MC1_0", "USER_PLAN" }));
            Assert.Contains("no plans were deleted", ex.Message);
            Assert.Equal(new[] { "MC1_0", "USER_PLAN" }, (await svc.ListQaPlansAsync("QA")).Select(p => p.PlanId).OrderBy(x => x).ToArray());

            await svc.DeleteQaPlansAsync("QA", new[] { "MC1_0" });
            Assert.Equal(new[] { "USER_PLAN" }, (await svc.ListQaPlansAsync("QA")).Select(p => p.PlanId).ToArray());
        }
    }
}
