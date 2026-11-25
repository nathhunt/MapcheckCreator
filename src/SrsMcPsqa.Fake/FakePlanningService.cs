using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Prescription;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.Fake
{
    /// <summary>
    /// In-memory <see cref="IPlanningService"/> with a synthetic patient. Write operations are simulated with delays.
    /// The "calculated" QA dose is the clinical dose moved into the phantom by (I_ph − I_clin), with a small systematic
    /// offset, scale and broader fall-off, so that fine-tuning has something to correct.
    /// </summary>
    public sealed class FakePlanningService : IPlanningService
    {
        private readonly FakePatient _patient = new FakePatient();
        private readonly Dictionary<string, List<FakeQaPlan>> _plansByCourse = new Dictionary<string, List<FakeQaPlan>>(StringComparer.OrdinalIgnoreCase)
        {
            ["C1"] = new List<FakeQaPlan>(),
            ["QA"] = new List<FakeQaPlan>(),
        };
        private readonly HashSet<ArrayOrientation> _copiedPhantoms = new HashSet<ArrayOrientation>();
        private readonly object _lock = new object();

        public bool SupportsWriting => true;

        /// <summary>Multiplier for simulated delays (0 = instant, useful in tests).</summary>
        public double DelayScale { get; set; } = 1.0;

        /// <summary>Systematic difference between "phantom calculation" and the clinical dose (mm), exercising fine-tune.</summary>
        public Vec3 CalculationOffset { get; set; } = new Vec3(0.8, -0.6, 1.2);

        public static readonly IReadOnlyDictionary<ArrayOrientation, Vec3> UserOrigins = new Dictionary<ArrayOrientation, Vec3>
        {
            [ArrayOrientation.Deg0] = new Vec3(-0.6, 12.3, -151.2),
            [ArrayOrientation.Deg270] = new Vec3(0.4, 11.8, -150.7),
        };

        private sealed class FakeQaPlan
        {
            public string Id = "";
            public ArrayOrientation Orientation;
            public Vec3 Isocentre;
            public QaPlanTag? Tag;
        }

        private static IReadOnlyList<double> Arc(double start, double stop, bool clockwise)
        {
            double span = clockwise ? ((stop - start) % 360 + 360) % 360 : ((start - stop) % 360 + 360) % 360;
            int n = Math.Max(2, (int)Math.Ceiling(span / 2) + 1);
            return Enumerable.Range(0, n).Select(i => ((start + (clockwise ? 1 : -1) * span * i / (n - 1)) % 360 + 360) % 360).ToList();
        }

        private const string DemoModel = "AcurosXB (demo)";
        private int _calculations;
        private const int DemoControlPointsPerArc = 90;
        private static readonly double[] DemoMus = { 412.37, 398.12, 405.66, 421.09 };

        /// <summary>Simulated calculation time (× <see cref="DelayScale"/>).</summary>
        public double DemoCalculationSeconds { get; set; } = 6.0;

        private async Task Step(IProgress<QaProgressEvent>? steps, string planId, string step, double seconds, string? detail, CancellationToken ct) =>
            await StepAsync(steps, planId, step, async () => { await Delay(seconds, ct).ConfigureAwait(false); return true; }, detail).ConfigureAwait(false);

        private static async Task<T> StepAsync<T>(IProgress<QaProgressEvent>? steps, string planId, string step, Func<Task<T>> work, string? detail = null)
        {
            steps?.Report(new QaProgressEvent { PlanId = planId, Step = step, State = QaStepState.Running });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var r = await work().ConfigureAwait(false);
                steps?.Report(new QaProgressEvent { PlanId = planId, Step = step, State = QaStepState.Done, Duration = sw.Elapsed, Detail = detail });
                return r;
            }
            catch (Exception ex)
            {
                steps?.Report(new QaProgressEvent { PlanId = planId, Step = step, State = QaStepState.Failed, Duration = sw.Elapsed, Detail = ex.Message });
                throw;
            }
        }

        private Task Delay(double seconds, CancellationToken ct) =>
            DelayScale <= 0 ? Task.CompletedTask : Task.Delay(TimeSpan.FromSeconds(seconds * DelayScale), ct);

        public async Task<PatientSummary> OpenPatientAsync(string patientId, CancellationToken ct = default)
        {
            await Delay(0.4, ct).ConfigureAwait(false);
            if (!string.Equals(patientId?.Trim(), _patient.PatientId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Patient '{patientId}' not found. The demo patient is '{_patient.PatientId}'.");
            lock (_lock)
            {
                return new PatientSummary
                {
                    Id = _patient.PatientId,
                    DisplayName = _patient.DisplayName,
                    CanModify = true,
                    Courses = new[]
                    {
                        new CourseSummary { Id = "C1", Plans = new[] { new PlanSummary { CourseId = "C1", PlanId = _patient.ClinicalPlanId } }
                            .Concat(Summaries("C1")).ToList() },
                        new CourseSummary { Id = "QA", Plans = Summaries("QA") },
                        new CourseSummary { Id = "C0_Old", IsCompleted = true, Plans = new[] { new PlanSummary { CourseId = "C0_Old", PlanId = "WBRT" } } },
                    },
                };
            }
        }

        private List<PlanSummary> Summaries(string course) =>
            _plansByCourse[course].Select(p => new PlanSummary { CourseId = course, PlanId = p.Id, IsQaPlan = p.Tag != null, Tag = p.Tag }).ToList();

        public Task<ClinicalPlanData> LoadClinicalPlanAsync(string courseId, string planId, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (courseId != _patient.ClinicalCourseId || planId != _patient.ClinicalPlanId)
                throw new InvalidOperationException($"Plan '{courseId}/{planId}' is not a calculated clinical plan in the demo patient.");
            return Task.Run(async () =>
            {
                progress?.Report("Reading plan, structures and dose...");
                await Delay(0.6, ct).ConfigureAwait(false);
                var maskSpacing = new Vec3(0.5, 0.5, 1.25);
                var ptvs = new List<PtvInfo>();
                foreach (var t in _patient.Ptvs)
                {
                    progress?.Report($"Rasterising {t.Id}...");
                    var box = new Box3(t.Centre - t.Radii, t.Centre + t.Radii).Expand(2);
                    var mask = StructureMask.FromPredicate(box, maskSpacing, t.Contains);
                    var (rx, src) = PrescriptionResolver.Resolve(t.Id, null, _patient.PlanTotalDoseGy);
                    ptvs.Add(new PtvInfo
                    {
                        Id = t.Id, VolumeCc = mask.VolumeCc, RxGy = rx, RxSource = src, Mask = mask,
                        Mesh = TriangleMesh.Ellipsoid(t.Centre, t.Radii),
                    });
                }
                progress?.Report("Extracting dose...");
                var region = _patient.Bounds(45);
                var dose = DoseGrid.FromFunction(Grid(region, 1.25), p => _patient.Dose(p));
                return new ClinicalPlanData
                {
                    CourseId = courseId,
                    PlanId = planId,
                    PlanUid = _patient.PlanUid,
                    Isocentre = _patient.Isocentre,
                    CalculationModel = "AcurosXB_1612",
                    AvailableCalculationModels = new[] { "AcurosXB_1612", "AcurosXB_1612_water", "AAA_1612" },
                    PlanTotalDoseGy = _patient.PlanTotalDoseGy,
                    Beams = new[]
                    {
                        // Like the test plan: a full arc at couch 0 and three half arcs with couch kicks.
                        new Core.Collision.BeamGeometry("Arc1", 0, Arc(181, 179, clockwise: true), Core.Collision.ArcDirection.Clockwise),
                        new Core.Collision.BeamGeometry("Arc2", 315, Arc(179, 0, clockwise: false), Core.Collision.ArcDirection.CounterClockwise),
                        new Core.Collision.BeamGeometry("Arc3", 45, Arc(181, 0, clockwise: true), Core.Collision.ArcDirection.Clockwise),
                        new Core.Collision.BeamGeometry("Arc4", 90, Arc(181, 0, clockwise: true), Core.Collision.ArcDirection.Clockwise),
                    },
                    Ptvs = ptvs,
                    Dose = dose,
                    Validation = new[]
                    {
                        new ValidationMessage(ValidationSeverity.Info, "Dose calculated."),
                        new ValidationMessage(ValidationSeverity.Info, "Single isocentre (6 arcs)."),
                        new ValidationMessage(ValidationSeverity.Info, "Treatment orientation HFS."),
                        new ValidationMessage(ValidationSeverity.Warning, "Synthetic demo data: not a real patient."),
                    },
                };
            }, ct);
        }

        public async Task<IReadOnlyList<PhantomImageInfo>> EnsurePhantomImagesAsync(TemplatePhantomSettings settings,
            IReadOnlyCollection<ArrayOrientation> orientations, IProgress<string>? progress = null, CancellationToken ct = default,
            IProgress<QaProgressEvent>? steps = null)
        {
            var result = new List<PhantomImageInfo>();
            foreach (var o in orientations)
            {
                bool copy;
                lock (_lock) copy = _copiedPhantoms.Add(o);
                string imageId = o == ArrayOrientation.Deg0 ? settings.Image0Id : settings.Image270Id;
                if (string.IsNullOrWhiteSpace(imageId)) imageId = o == ArrayOrientation.Deg0 ? "SMC_0" : "SMC_270";
                if (copy)
                {
                    progress?.Report($"Copying phantom image '{imageId}' from template patient '{settings.PatientId}'...");
                    await Step(steps, QaSteps.PhantomImages, $"Copy {imageId}", 1.0, $"from {settings.PatientId}", ct).ConfigureAwait(false);
                }
                else
                {
                    progress?.Report($"Phantom image '{imageId}' already in patient; reusing it.");
                    steps?.Report(new QaProgressEvent { PlanId = QaSteps.PhantomImages, Step = $"Reuse {imageId}", State = QaStepState.Done, Detail = "already in the patient" });
                }
                result.Add(new PhantomImageInfo
                {
                    Orientation = o, ImageId = imageId, StructureSetId = imageId, UserOrigin = UserOrigins[o], WasCopied = copy,
                });
            }
            return result;
        }

        public async Task<IReadOnlyList<ValidationMessage>> ValidateTemplateAsync(TemplatePhantomSettings settings, CancellationToken ct = default)
        {
            await Delay(0.5, ct).ConfigureAwait(false);
            var list = new List<ValidationMessage>();
            void Need(string value, string what)
            {
                list.Add(string.IsNullOrWhiteSpace(value)
                    ? new ValidationMessage(ValidationSeverity.Error, $"{what} is not set.")
                    : new ValidationMessage(ValidationSeverity.Info, $"{what} '{value}' found (demo)."));
            }
            Need(settings.PatientId, "Template patient");
            Need(settings.Image0Id, "0° image");
            Need(settings.Image270Id, "270° image");
            return list;
        }

        public async Task<QaPlanResult> CreateQaPlanAsync(QaPlanRequest request, IProgress<string>? progress = null, CancellationToken ct = default,
            IProgress<QaProgressEvent>? steps = null)
        {
            lock (_lock)
            {
                if (!_plansByCourse.TryGetValue(request.QaCourseId, out var plans))
                    throw new InvalidOperationException($"Course '{request.QaCourseId}' not found.");
                if (plans.Any(p => string.Equals(p.Id, request.PlanId, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Plan ID '{request.PlanId}' already exists in course '{request.QaCourseId}'.");
                if (request.PlanId.Length > QaPlanNaming.MaxIdLength)
                    throw new InvalidOperationException($"Plan ID '{request.PlanId}' is longer than {QaPlanNaming.MaxIdLength} characters.");
            }
            ct.ThrowIfCancellationRequested();
            ct = CancellationToken.None; // like Eclipse: once a plan is started it is finished; Cancel acts between plans
            progress?.Report($"{request.PlanId}: creating verification plan...");
            await Step(steps, request.PlanId, "Create verification plan", 0.4, "verifies the demo plan", ct).ConfigureAwait(false);
            await Step(steps, request.PlanId, "Set ID, prescription and calculation model", 0.2, $"1 × 20 Gy, {DemoModel}", ct).ConfigureAwait(false);
            progress?.Report($"{request.PlanId}: copying {DemoMus.Length} beams at isocentre {request.Isocentre}...");
            await Step(steps, request.PlanId, "Copy beams", 0.6, $"{DemoMus.Length} beams, clinical order", ct).ConfigureAwait(false);
            await Step(steps, request.PlanId, "Save", 0.2, null, ct).ConfigureAwait(false);
            lock (_lock)
            {
                _plansByCourse[request.QaCourseId].Add(new FakeQaPlan
                {
                    Id = request.PlanId, Orientation = request.Orientation, Isocentre = request.Isocentre,
                    Tag = new QaPlanTag(_patient.PlanUid, request.Orientation, request.AssignedPtvIds),
                });
            }
            return await Calculate(request.QaCourseId, request.PlanId, request.Orientation, request.Isocentre, request.DoseRegion, progress, ct, steps)
                .ConfigureAwait(false);
        }

        public async Task<QaPlanResult> MoveIsocentreAsync(string courseId, string planId, Vec3 newIsocentre, Box3 doseRegion,
            IProgress<string>? progress = null, CancellationToken ct = default, IProgress<QaProgressEvent>? steps = null)
        {
            FakeQaPlan plan;
            lock (_lock)
            {
                plan = Find(courseId, planId) ?? throw new InvalidOperationException($"QA plan '{courseId}/{planId}' not found.");
                CheckTag(plan, "it is left untouched");
                plan.Isocentre = newIsocentre;
            }
            ct.ThrowIfCancellationRequested();
            ct = CancellationToken.None;
            progress?.Report($"{planId}: moving isocentre to {newIsocentre}...");
            await Step(steps, planId, "Move isocentre", 0.3, $"to {newIsocentre}", ct).ConfigureAwait(false);
            await Step(steps, planId, "Save", 0.2, null, ct).ConfigureAwait(false);
            return await Calculate(courseId, planId, plan.Orientation, newIsocentre, doseRegion, progress, ct, steps).ConfigureAwait(false);
        }

        private async Task<QaPlanResult> Calculate(string courseId, string planId, ArrayOrientation o, Vec3 iso, Box3 region,
            IProgress<string>? progress, CancellationToken ct, IProgress<QaProgressEvent>? steps)
        {
            progress?.Report($"{planId}: calculating dose with clinical MUs (cannot be interrupted)...");
            int cps = DemoMus.Length * DemoControlPointsPerArc;
            string detail = $"{DemoModel}, {DemoMus.Length} beams, {cps} control points";
            steps?.Report(new QaProgressEvent
            {
                PlanId = planId, Step = "Calculate dose", State = QaStepState.Running, IsCalculation = true,
                Model = DemoModel, Beams = DemoMus.Length, ControlPoints = cps, Detail = detail,
            });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // Every other demo calculation imitates Eclipse writing progress to its trace output, so both panel modes can be seen.
            bool eclipseProgress = System.Threading.Interlocked.Increment(ref _calculations) % 2 == 0;
            const int ticks = 20;
            for (int i = 1; i <= ticks; i++)
            {
                await Delay(DemoCalculationSeconds / ticks, ct).ConfigureAwait(false);
                if (eclipseProgress && i < ticks)
                    steps?.Report(new QaProgressEvent
                    {
                        PlanId = planId, Step = "Calculate dose", State = QaStepState.Running, IsCalculation = true,
                        EclipsePercent = 100.0 * i / ticks, EclipseMessage = i % 5 == 1 ? $"Calculating field Arc{1 + (i - 1) * DemoMus.Length / ticks}" : null,
                    });
            }
            steps?.Report(new QaProgressEvent
            {
                PlanId = planId, Step = "Calculate dose", State = QaStepState.Done, IsCalculation = true,
                Model = DemoModel, Beams = DemoMus.Length, ControlPoints = cps, Detail = detail, Duration = sw.Elapsed,
            });
            steps?.Report(new QaProgressEvent
            {
                PlanId = planId, Step = "Check MUs", State = QaStepState.Done, Detail = "equal to the clinical MUs (max difference 0.000 MU)",
                MuResults = DemoMus.Select((mu, i) => new BeamMuResult($"Arc{i + 1}", mu, mu, 0.05)).ToList(),
            });
            var shift = iso - _patient.Isocentre + CalculationOffset;
            var dose = await StepAsync(steps, planId, "Read calculated dose", () => Task.Run(() =>
                DoseGrid.FromFunction(Grid(region, 1.0), q => _patient.Dose(q - shift, sigma: 2.9, scale: 0.985)), ct)).ConfigureAwait(false);
            await Step(steps, planId, "Save", 0.2, null, ct).ConfigureAwait(false);
            progress?.Report($"{planId}: calculation finished; saved.");
            return new QaPlanResult
            {
                CourseId = courseId, PlanId = planId, Orientation = o, Isocentre = iso, CalculationSucceeded = true,
                Messages = new[] { "Simulated calculation (demo data)." }, Dose = dose,
            };
        }

        public Task<IReadOnlyList<ExistingQaPlan>> FindExistingQaPlansAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            List<(string Course, FakeQaPlan Plan)> plans;
            lock (_lock) plans = _plansByCourse.SelectMany(kv => kv.Value.Select(p => (kv.Key, p))).Where(x => x.p.Tag?.SourcePlanUid == _patient.PlanUid).ToList();
            return Task.Run<IReadOnlyList<ExistingQaPlan>>(() => plans.Select(x =>
            {
                var o = x.Plan.Orientation;
                var region = QaPlanRequests.DoseRegion(o, UserOrigins[o]);
                var shift = x.Plan.Isocentre - _patient.Isocentre + CalculationOffset;
                var dose = DoseGrid.FromFunction(Grid(region, 1.0), q => _patient.Dose(q - shift, sigma: 2.9, scale: 0.985));
                string image = o == ArrayOrientation.Deg0 ? "SMC_0" : "SMC_270";
                return new ExistingQaPlan
                {
                    Result = new QaPlanResult
                    {
                        CourseId = x.Course, PlanId = x.Plan.Id, Orientation = o, Isocentre = x.Plan.Isocentre, CalculationSucceeded = true, Dose = dose,
                        Messages = new[] { "Found by its tag (demo data)." },
                    },
                    Phantom = new PhantomImageInfo { Orientation = o, ImageId = image, StructureSetId = image, UserOrigin = UserOrigins[o] },
                    AssignedPtvIds = x.Plan.Tag!.PtvIds,
                };
            }).ToList(), ct);
        }

        public async Task<CourseSummary> EnsureCourseAsync(string courseId, CancellationToken ct = default)
        {
            await Delay(0.3, ct).ConfigureAwait(false);
            lock (_lock)
                if (!_plansByCourse.ContainsKey(courseId)) _plansByCourse[courseId] = new List<FakeQaPlan>();
            return new CourseSummary { Id = courseId };
        }

        public Task<IReadOnlyList<PlanSummary>> ListQaPlansAsync(string courseId, CancellationToken ct = default)
        {
            lock (_lock)
            {
                IReadOnlyList<PlanSummary> list = _plansByCourse.ContainsKey(courseId) ? Summaries(courseId).Where(s => s.IsQaPlan).ToList() : new List<PlanSummary>();
                return Task.FromResult(list);
            }
        }

        public Task<IReadOnlyList<string>> ListPlanIdsAsync(string courseId, CancellationToken ct = default)
        {
            lock (_lock)
            {
                var c = _plansByCourse.Keys.FirstOrDefault(k => string.Equals(k, courseId, StringComparison.OrdinalIgnoreCase));
                IReadOnlyList<string> ids = c == null ? new List<string>() : _plansByCourse[c].Select(p => p.Id).ToList();
                return Task.FromResult(ids);
            }
        }

        public async Task DeleteQaPlansAsync(string courseId, IReadOnlyCollection<string> planIds, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            // Like Eclipse: check every plan before removing any, so a refusal deletes nothing.
            var toDelete = new List<FakeQaPlan>();
            lock (_lock)
            {
                foreach (var id in planIds)
                {
                    var p = Find(courseId, id);
                    if (p == null) continue;
                    CheckTag(p, "no plans were deleted");
                    toDelete.Add(p);
                }
            }
            foreach (var p in toDelete)
            {
                lock (_lock) _plansByCourse[courseId].Remove(p);
                progress?.Report($"Deleted {p.Id}.");
                await Delay(0.2, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>Like Eclipse: only this app's QA plans of the loaded clinical plan may be changed or deleted.</summary>
        private void CheckTag(FakeQaPlan p, string consequence)
        {
            if (p.Tag == null) throw new InvalidOperationException($"Plan '{p.Id}' has no app tag; {consequence}.");
            if (p.Tag.SourcePlanUid != _patient.PlanUid) throw new InvalidOperationException($"Plan '{p.Id}' is a QA plan for a different clinical plan; {consequence}.");
        }

        /// <summary>Test hook: a plan in <paramref name="courseId"/> made outside the app (no tag) or tagged for another plan.</summary>
        internal void AddPlanForTests(string courseId, string planId, QaPlanTag? tag = null)
        {
            lock (_lock)
            {
                if (!_plansByCourse.ContainsKey(courseId)) _plansByCourse[courseId] = new List<FakeQaPlan>();
                _plansByCourse[courseId].Add(new FakeQaPlan { Id = planId, Tag = tag });
            }
        }

        private FakeQaPlan? Find(string courseId, string planId) =>
            _plansByCourse.TryGetValue(courseId, out var list)
                ? list.FirstOrDefault(p => string.Equals(p.Id, planId, StringComparison.OrdinalIgnoreCase))
                : null;

        private static GridGeometry Grid(Box3 region, double spacing)
        {
            int nx = Math.Max(2, (int)Math.Ceiling(region.Size.X / spacing) + 1);
            int ny = Math.Max(2, (int)Math.Ceiling(region.Size.Y / spacing) + 1);
            int nz = Math.Max(2, (int)Math.Ceiling(region.Size.Z / spacing) + 1);
            return new GridGeometry(region.Min, new Vec3(spacing, spacing, spacing), nx, ny, nz);
        }
    }
}
