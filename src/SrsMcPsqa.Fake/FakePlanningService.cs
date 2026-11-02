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
            _plansByCourse[course].Select(p => new PlanSummary { CourseId = course, PlanId = p.Id, IsQaPlan = true, Tag = p.Tag }).ToList();

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
                    PlanTotalDoseGy = _patient.PlanTotalDoseGy,
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
            IReadOnlyCollection<ArrayOrientation> orientations, IProgress<string>? progress = null, CancellationToken ct = default)
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
                    await Delay(1.0, ct).ConfigureAwait(false);
                }
                else progress?.Report($"Phantom image '{imageId}' already in patient; reusing it.");
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
            Need(settings.StudyId, "Study");
            Need(settings.Image0Id, "0° image");
            Need(settings.Image270Id, "270° image");
            return list;
        }

        public async Task<QaPlanResult> CreateQaPlanAsync(QaPlanRequest request, IProgress<string>? progress = null, CancellationToken ct = default)
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
            progress?.Report($"{request.PlanId}: creating verification plan...");
            await Delay(0.4, ct).ConfigureAwait(false);
            progress?.Report($"{request.PlanId}: copying 6 beams at isocentre {request.Isocentre}...");
            await Delay(0.6, ct).ConfigureAwait(false);
            lock (_lock)
            {
                _plansByCourse[request.QaCourseId].Add(new FakeQaPlan
                {
                    Id = request.PlanId, Orientation = request.Orientation, Isocentre = request.Isocentre,
                    Tag = new QaPlanTag(_patient.PlanUid, request.Orientation, request.AssignedPtvIds),
                });
            }
            return await Calculate(request.QaCourseId, request.PlanId, request.Orientation, request.Isocentre, request.DoseRegion, progress, ct)
                .ConfigureAwait(false);
        }

        public async Task<QaPlanResult> MoveIsocentreAsync(string courseId, string planId, Vec3 newIsocentre, Box3 doseRegion,
            IProgress<string>? progress = null, CancellationToken ct = default)
        {
            FakeQaPlan plan;
            lock (_lock)
            {
                plan = Find(courseId, planId) ?? throw new InvalidOperationException($"QA plan '{courseId}/{planId}' not found.");
                plan.Isocentre = newIsocentre;
            }
            progress?.Report($"{planId}: moving isocentre to {newIsocentre}...");
            await Delay(0.3, ct).ConfigureAwait(false);
            return await Calculate(courseId, planId, plan.Orientation, newIsocentre, doseRegion, progress, ct).ConfigureAwait(false);
        }

        private async Task<QaPlanResult> Calculate(string courseId, string planId, ArrayOrientation o, Vec3 iso, Box3 region,
            IProgress<string>? progress, CancellationToken ct)
        {
            progress?.Report($"{planId}: calculating dose with clinical MUs (cannot be interrupted)...");
            await Delay(1.5, ct).ConfigureAwait(false);
            var shift = iso - _patient.Isocentre + CalculationOffset;
            var dose = await Task.Run(() =>
                DoseGrid.FromFunction(Grid(region, 1.0), q => _patient.Dose(q - shift, sigma: 2.9, scale: 0.985)), ct).ConfigureAwait(false);
            progress?.Report($"{planId}: calculation finished; saved.");
            return new QaPlanResult
            {
                CourseId = courseId, PlanId = planId, Orientation = o, Isocentre = iso, CalculationSucceeded = true,
                Messages = new[] { "Simulated calculation (demo data)." }, Dose = dose,
            };
        }

        public Task<IReadOnlyList<PlanSummary>> ListQaPlansAsync(string courseId, CancellationToken ct = default)
        {
            lock (_lock)
            {
                IReadOnlyList<PlanSummary> list = _plansByCourse.ContainsKey(courseId) ? Summaries(courseId) : new List<PlanSummary>();
                return Task.FromResult(list);
            }
        }

        public async Task DeleteQaPlansAsync(string courseId, IReadOnlyCollection<string> planIds, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            foreach (var id in planIds)
            {
                lock (_lock)
                {
                    var p = Find(courseId, id);
                    if (p == null) continue;
                    if (p.Tag == null) throw new InvalidOperationException($"Plan '{id}' has no app tag; refusing to delete it.");
                    _plansByCourse[courseId].Remove(p);
                }
                progress?.Report($"Deleted {id}.");
                await Delay(0.2, ct).ConfigureAwait(false);
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
