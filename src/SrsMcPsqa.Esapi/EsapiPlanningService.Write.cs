using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SrsMcPsqa.Core.Services;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using EsapiApplication = VMS.TPS.Common.Model.API.Application;
// Core DTOs aliased explicitly: ESAPI has types with the same names.
using PhantomImageInfo = SrsMcPsqa.Core.Services.PhantomImageInfo;
using QaPlanRequest = SrsMcPsqa.Core.Services.QaPlanRequest;
using QaPlanResult = SrsMcPsqa.Core.Services.QaPlanResult;
using TemplatePhantomSettings = SrsMcPsqa.Core.Services.TemplatePhantomSettings;
using ArrayOrientation = SrsMcPsqa.Core.Geometry.ArrayOrientation;
using Vec3 = SrsMcPsqa.Core.Geometry.Vec3;
using Box3 = SrsMcPsqa.Core.Geometry.Box3;
using QaPlanTag = SrsMcPsqa.Core.Models.QaPlanTag;
using QaPlanNaming = SrsMcPsqa.Core.Models.QaPlanNaming;

namespace SrsMcPsqa.Esapi
{
    /// <summary>
    /// Phase 4 write path, following what the Phase 0 spike verified on Citrix (ESAPI 18.1):
    /// <list type="bullet">
    /// <item>Phantom images: <c>CopyImageFromOtherPatient</c> (new study; BODY and its HU come across), reused when present.</item>
    /// <item>QA plan: <c>AddExternalPlanSetupAsVerificationPlan</c> (empty, linked, inherits the calculation model),
    /// prescription 1 fraction × clinical dose/fraction, ID from the naming pattern, app tag in the Comment.</item>
    /// <item>Beams copied in the clinical treatment order at the QA isocentre (<see cref="EsapiBeamCopier"/>).</item>
    /// <item>MUs: preset MUs passed to <c>CalculateDoseWithPresetValues</c> are ignored on verification plans, so beam
    /// weights are set to clinical MU ÷ (dose/fraction × 100 MU/Gy); every beam's MU is checked after calculation,
    /// corrected once if needed, otherwise the operation fails.</item>
    /// <item>Isocentre moves: <c>BeamParameters.Isocenter</c> + <c>ApplyParameters</c>, then recalculation with the same MU check.</item>
    /// </list>
    /// </summary>
    public sealed partial class EsapiPlanningService
    {
        /// <summary>Largest MU difference per beam accepted after calculation.</summary>
        public const double MuTolerance = 0.05;

        /// <summary>Save to Eclipse after every completed write (default). When false, <see cref="SaveAsync"/> must be called.</summary>
        public bool AutoSave { get; set; } = true;

        private readonly Dictionary<string, string> _studyCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ helpers
        private void RequireModifiable()
        {
            RequirePatient();
            if (!_canModify)
                throw new InvalidOperationException("Eclipse doesn't allow changes to this patient: " + (_modifyError ?? "unknown reason") + ". Close it in Eclipse if needed and re-open it here.");
        }

        private ExternalPlanSetup RequireClinicalPlan()
        {
            if (_clinicalCourseId == null || _clinicalPlanId == null) throw new InvalidOperationException("No clinical plan loaded.");
            return RequirePatient().Courses.FirstOrDefault(c => c.Id == _clinicalCourseId)?.ExternalPlanSetups.FirstOrDefault(p => p.Id == _clinicalPlanId)
                   ?? throw new InvalidOperationException($"Clinical plan '{_clinicalCourseId}/{_clinicalPlanId}' not found.");
        }

        public Task<CourseSummary> EnsureCourseAsync(string courseId, CancellationToken ct = default) =>
            _worker.RunAsync($"prepare course '{courseId}'", app =>
            {
                RequireModifiable();
                if (string.IsNullOrWhiteSpace(courseId)) throw new InvalidOperationException("No QA course ID is set (Settings).");
                if (courseId.Length > 16) throw new InvalidOperationException($"Course ID '{courseId}' is longer than 16 characters.");
                var patient = RequirePatient();
                var existing = patient.Courses.FirstOrDefault(c => string.Equals(c.Id, courseId, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (existing.IsCompleted()) throw new InvalidOperationException($"Course '{existing.Id}' exists but is completed; choose another QA course.");
                    return new CourseSummary { Id = existing.Id };
                }
                if (!patient.CanAddCourse()) throw new InvalidOperationException("Eclipse doesn't allow adding a course to this patient.");
                var course = patient.AddCourse();
                course.Id = courseId;
                _log($"Created course '{courseId}'.");
                SaveIfAuto(app, $"creating course '{courseId}'");
                return new CourseSummary { Id = course.Id };
            });

        private Course RequireCourse(string courseId)
        {
            var c = RequirePatient().Courses.FirstOrDefault(x => x.Id == courseId) ?? throw new InvalidOperationException($"Course '{courseId}' not found.");
            if (c.IsCompleted()) throw new InvalidOperationException($"Course '{courseId}' is completed; choose an open course for the QA plans.");
            return c;
        }

        /// <summary>A QA plan created by this app (tagged); refuses untagged plans.</summary>
        private static ExternalPlanSetup RequireTaggedPlan(Course course, string planId)
        {
            var plan = course.ExternalPlanSetups.FirstOrDefault(p => string.Equals(p.Id, planId, StringComparison.OrdinalIgnoreCase))
                       ?? throw new InvalidOperationException($"Plan '{planId}' not found in course '{course.Id}'.");
            if (!QaPlanTag.TryParse(plan.Comment, out _))
                throw new InvalidOperationException($"Plan '{planId}' was not created by this app (no tag in its Comment); it is left untouched.");
            return plan;
        }

        private void SaveIfAuto(EsapiApplication app, string what)
        {
            if (!AutoSave) { _log($"Not saving after {what} (auto-save off)."); return; }
            app.SaveModifications();
            _log($"Saved after {what}.");
        }

        /// <summary>Save pending changes to Eclipse (used when auto-save is off).</summary>
        public Task SaveAsync() => _worker.RunAsync("save changes", app =>
        {
            if (_patient != null && _canModify) app.SaveModifications();
            return true;
        });

        private static double ToGy(DoseValue dv) => EsapiExtraction.ToGy(dv, null);

        // ------------------------------------------------------------------ phantom images
        public Task<IReadOnlyList<PhantomImageInfo>> EnsurePhantomImagesAsync(TemplatePhantomSettings settings, IReadOnlyCollection<ArrayOrientation> orientations,
            IProgress<string>? progress = null, CancellationToken ct = default, IProgress<QaProgressEvent>? steps = null) =>
            _worker.RunAsync<IReadOnlyList<PhantomImageInfo>>("prepare the phantom images", app =>
            {
                RequireModifiable();
                if (string.IsNullOrWhiteSpace(settings.PatientId)) throw new InvalidOperationException("The template phantom patient is not set (Settings).");
                var result = new List<PhantomImageInfo>();
                var toCopy = new List<ArrayOrientation>();

                foreach (var o in orientations.Distinct())
                {
                    var existing = FindPhantomInPatient(settings.ImageId(o));
                    if (existing != null)
                    {
                        progress?.Report($"Phantom image '{existing.Image.Id}' is already in the patient; reusing it.");
                        steps?.Report(new QaProgressEvent { PlanId = QaSteps.PhantomImages, Step = $"Reuse {existing.Image.Id}", State = QaStepState.Done, Detail = "already in the patient" });
                        result.Add(Info(o, existing, copied: false));
                    }
                    else toCopy.Add(o);
                }
                if (toCopy.Count == 0) return result;

                // Studies left empty in Settings are looked up in the template patient (one patient open at a time).
                var studies = QaSteps.Run(steps, QaSteps.PhantomImages, "Find template studies", () => ResolveTemplateStudies(app, settings, toCopy, progress));
                foreach (var o in toCopy)
                {
                    ct.ThrowIfCancellationRequested();
                    string imageId = settings.ImageId(o);
                    progress?.Report($"Copying phantom image '{imageId}' from '{settings.PatientId}' (study '{studies[o]}')...");
                    var sw = Stopwatch.StartNew();
                    var ss = QaSteps.Run(steps, QaSteps.PhantomImages, $"Copy {imageId}", () =>
                    {
                        var copied = RequirePatient().CopyImageFromOtherPatient(settings.PatientId, studies[o], imageId)
                                     ?? throw new InvalidOperationException($"Copying image '{imageId}' returned no structure set.");
                        if (!HasBody(copied))
                            throw new InvalidOperationException($"The copied image '{imageId}' has no BODY/EXTERNAL structure, so it can't be calculated. Check the template patient.");
                        return copied;
                    }, _ => $"from {settings.PatientId}, study {studies[o]}");
                    _log($"Copied phantom image '{imageId}' in {sw.Elapsed.TotalSeconds:F1} s (structure set {ss.Id}, UID {ss.UID}).");
                    result.Add(Info(o, ss, copied: true));
                    QaSteps.Run(steps, QaSteps.PhantomImages, "Save", () => SaveIfAuto(app, $"copying '{imageId}'"));
                }
                return result;
            });

        private static PhantomImageInfo Info(ArrayOrientation o, StructureSet ss, bool copied) => new PhantomImageInfo
        {
            Orientation = o, ImageId = ss.Image.Id, StructureSetId = ss.Id, StructureSetUid = ss.UID,
            UserOrigin = ss.Image.UserOrigin.ToVec3(), WasCopied = copied,
        };

        private static bool HasBody(StructureSet ss) =>
            ss.Structures.Any(s => !s.IsEmpty && (s.DicomType == "EXTERNAL" || s.DicomType == "BODY"));

        /// <summary>A phantom image already in the patient: same image ID, with a structure set that has a BODY.</summary>
        private StructureSet? FindPhantomInPatient(string imageId) =>
            RequirePatient().StructureSets.Where(s => s.Image != null && s.Image.Id == imageId && HasBody(s)).LastOrDefault();

        private Dictionary<ArrayOrientation, string> ResolveTemplateStudies(EsapiApplication app, TemplatePhantomSettings settings,
            IReadOnlyCollection<ArrayOrientation> orientations, IProgress<string>? progress)
        {
            var result = new Dictionary<ArrayOrientation, string>();
            var missing = new List<ArrayOrientation>();
            foreach (var o in orientations)
            {
                string key = settings.PatientId + "|" + settings.ImageId(o);
                if (!string.IsNullOrWhiteSpace(settings.StudyId(o))) result[o] = settings.StudyId(o);
                else if (_studyCache.TryGetValue(key, out var cached)) result[o] = cached;
                else missing.Add(o);
            }
            if (missing.Count == 0) return result;

            progress?.Report($"Looking up the template images in '{settings.PatientId}'...");
            string current = _patientId ?? throw new InvalidOperationException("No patient is open.");
            app.SaveModifications(); // closing the patient would discard unsaved changes
            app.ClosePatient();
            _patient = null;
            try
            {
                var tp = app.OpenPatientById(settings.PatientId) ?? throw new InvalidOperationException($"Template patient '{settings.PatientId}' not found.");
                foreach (var o in missing)
                {
                    var (study, img, error) = EsapiExtraction.FindTemplateImage(tp, null, settings.ImageId(o));
                    if (img == null) throw new InvalidOperationException($"Template {(o == ArrayOrientation.Deg0 ? "0°" : "270°")} image: {error}");
                    result[o] = study!.Id;
                    _studyCache[settings.PatientId + "|" + settings.ImageId(o)] = study.Id;
                }
            }
            finally
            {
                app.ClosePatient();
                Open(app, current);
            }
            if (!_canModify) throw new InvalidOperationException("Re-opening the patient after reading the template failed to allow changes: " + _modifyError);
            return result;
        }

        // ------------------------------------------------------------------ create
        public Task<QaPlanResult> CreateQaPlanAsync(QaPlanRequest request, IProgress<string>? progress = null, CancellationToken ct = default,
            IProgress<QaProgressEvent>? steps = null) =>
            _worker.RunAsync($"create QA plan '{request.PlanId}'", app =>
            {
                RequireModifiable();
                var clinical = RequireClinicalPlan();
                var course = RequireCourse(request.QaCourseId);
                if (request.PlanId.Length > QaPlanNaming.MaxIdLength)
                    throw new InvalidOperationException($"Plan ID '{request.PlanId}' is longer than {QaPlanNaming.MaxIdLength} characters.");
                if (course.PlanSetups.Any(p => string.Equals(p.Id, request.PlanId, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Plan ID '{request.PlanId}' already exists in course '{course.Id}'.");
                var ss = RequirePatient().StructureSets.FirstOrDefault(s => s.UID == request.StructureSetUid)
                         ?? throw new InvalidOperationException("The phantom structure set was not found in the patient.");

                progress?.Report($"{request.PlanId}: creating the verification plan on '{ss.Image.Id}'...");
                var qa = QaSteps.Run(steps, request.PlanId, "Create verification plan", () => course.AddExternalPlanSetupAsVerificationPlan(ss, clinical),
                    _ => $"on {ss.Image.Id}, verifies {clinical.Id}");
                try
                {
                    return BuildQaPlan(app, clinical, course, qa, request, progress, ct, steps);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // Don't leave a half-made plan behind that the app doesn't list.
                    string note;
                    try
                    {
                        course.RemovePlanSetup(qa);
                        SaveIfAuto(app, $"removing the incomplete '{request.PlanId}'");
                        note = $"The incomplete plan '{request.PlanId}' was removed.";
                    }
                    catch (Exception removeEx)
                    {
                        note = $"The incomplete plan '{request.PlanId}' could not be removed ({removeEx.Message}); delete it in Eclipse.";
                    }
                    throw new InvalidOperationException($"{ex.Message} {note}", ex);
                }
            });

        private QaPlanResult BuildQaPlan(EsapiApplication app, ExternalPlanSetup clinical, Course course, ExternalPlanSetup qa, QaPlanRequest request,
            IProgress<string>? progress, CancellationToken ct, IProgress<QaProgressEvent>? steps)
        {
                // Once the plan exists, finish it (calculate, read, save): Cancel takes effect between plans, so it
                // never leaves an uncalculated or unsaved plan behind.
                ct = CancellationToken.None;
                var messages = new List<string>();
                QaSteps.Run(steps, request.PlanId, "Set ID, prescription and calculation model", () =>
                {
                    qa.Id = request.PlanId;
                    qa.Comment = new QaPlanTag(clinical.UID, request.Orientation, request.AssignedPtvIds).Format();
                    // PSQA: one fraction at the clinical dose per fraction (the clinical MUs are per fraction).
                    qa.SetPrescription(1, clinical.DosePerFraction, clinical.TreatmentPercentage);
                    messages.Add($"Prescription 1 × {clinical.DosePerFraction}.");
                    messages.AddRange(MatchCalculationModel(clinical, qa));
                    return qa.GetCalculationModel(CalculationType.PhotonVolumeDose);
                }, model => $"1 × {clinical.DosePerFraction}, {model}");

                progress?.Report($"{request.PlanId}: copying beams at isocentre {request.Isocentre}...");
                var iso = new VVector(request.Isocentre.X, request.Isocentre.Y, request.Isocentre.Z);
                QaSteps.Run(steps, request.PlanId, "Copy beams", () =>
                {
                    EsapiBeamCopier.CopyAll(clinical, qa, iso, m => { _log(m); progress?.Report($"{request.PlanId}: {m}"); });
                    return qa.Beams.Count(b => !b.IsSetupField);
                }, n => $"{n} beams, clinical order");
                QaSteps.Run(steps, request.PlanId, "Save", () => SaveIfAuto(app, $"creating '{request.PlanId}'"));

                messages.AddRange(CalculateWithClinicalMus(clinical, qa, request.PlanId, progress, ct, steps));
                var dose = QaSteps.Run(steps, request.PlanId, "Read calculated dose", () => ExtractQaDose(qa, request.DoseRegion, progress, ct));
                QaSteps.Run(steps, request.PlanId, "Save", () => SaveIfAuto(app, $"calculating '{request.PlanId}'"));
                return new QaPlanResult
                {
                    CourseId = course.Id, PlanId = qa.Id, Orientation = request.Orientation, Isocentre = request.Isocentre,
                    CalculationSucceeded = true, Messages = messages, Dose = dose,
                };
        }

        /// <summary>
        /// The verification plan inherits the clinical calculation model (seen on Citrix). If the clinical plan reports a
        /// model, make sure it's the same, and copy its options (incl. grid size) where they differ.
        /// </summary>
        private IEnumerable<string> MatchCalculationModel(ExternalPlanSetup clinical, ExternalPlanSetup qa)
        {
            var notes = new List<string>();
            string clinModel = clinical.GetCalculationModel(CalculationType.PhotonVolumeDose);
            if (string.IsNullOrEmpty(clinModel)) clinModel = clinical.PhotonCalculationModel ?? "";
            string qaModel = qa.GetCalculationModel(CalculationType.PhotonVolumeDose);
            if (clinModel.Length > 0 && clinModel != qaModel)
            {
                qa.SetCalculationModel(CalculationType.PhotonVolumeDose, clinModel);
                qaModel = clinModel;
            }
            notes.Add($"Calculation model {qaModel}.");
            if (qaModel.Length == 0) throw new InvalidOperationException("The QA plan has no photon calculation model.");

            var clinOpts = clinical.PhotonCalculationOptions ?? new Dictionary<string, string>();
            if (clinOpts.Count == 0 && clinModel.Length > 0) clinOpts = clinical.GetCalculationOptions(clinModel);
            var qaOpts = qa.GetCalculationOptions(qaModel);
            foreach (var kv in clinOpts)
            {
                qaOpts.TryGetValue(kv.Key, out var current);
                if (current == kv.Value) continue;
                if (!qa.SetCalculationOption(qaModel, kv.Key, kv.Value))
                    throw new InvalidOperationException($"Could not set calculation option {kv.Key} = {kv.Value} on the QA plan.");
                notes.Add($"Option {kv.Key} = {kv.Value}.");
            }
            return notes;
        }

        /// <summary>
        /// Calculate so that every beam gets exactly its clinical MU: weights = clinical MU ÷ (dose/fraction × 100 MU/Gy),
        /// check after calculation, correct once, else fail. (Preset values are passed too, in case a future version honours them.)
        /// </summary>
        private IEnumerable<string> CalculateWithClinicalMus(ExternalPlanSetup clinical, ExternalPlanSetup qa, string planId, IProgress<string>? progress,
            CancellationToken ct, IProgress<QaProgressEvent>? steps)
        {
            var clinicalMu = clinical.Beams.Where(b => !b.IsSetupField).ToDictionary(b => b.Id, b => b.Meterset);
            var beams = qa.Beams.Where(b => !b.IsSetupField).ToList();
            var unknown = beams.Where(b => !clinicalMu.ContainsKey(b.Id)).Select(b => b.Id).ToList();
            if (unknown.Count > 0) throw new InvalidOperationException($"QA beams without a clinical beam of the same ID: {string.Join(", ", unknown)}.");
            var presets = beams.Select(b => new KeyValuePair<string, MetersetValue>(b.Id, clinicalMu[b.Id])).ToList();

            double dpf = ToGy(qa.DosePerFraction);
            if (!(dpf > 0)) throw new InvalidOperationException("The QA plan has no dose per fraction, so its MUs can't be set.");
            foreach (var b in beams) SetWeight(b, clinicalMu[b.Id].Value / (dpf * 100.0));

            var notes = new List<string>();
            string model = qa.GetCalculationModel(CalculationType.PhotonVolumeDose);
            int controlPoints = beams.Sum(b => b.ControlPoints.Count());
            for (int pass = 1; pass <= 2; pass++)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"{planId}: calculating dose with the clinical MUs{(pass == 2 ? " (correction pass)" : "")}; this can take several minutes and can't be interrupted...");
                string stepName = pass == 1 ? "Calculate dose" : "Recalculate (MU correction)";
                string calcDetail = $"{model}, {beams.Count} beams, {controlPoints} control points";
                steps?.Report(new QaProgressEvent
                {
                    PlanId = planId, Step = stepName, State = QaStepState.Running, IsCalculation = true,
                    Model = model, Beams = beams.Count, ControlPoints = controlPoints, Detail = calcDetail,
                });
                var sw = Stopwatch.StartNew();
                CalculationResult res;
                var trace = new CalculationTrace(planId, stepName, steps, _log);
                try
                {
                    using (EclipseTraceListener.Attach(trace.OnLine))
                        res = qa.CalculateDoseWithPresetValues(presets);
                }
                catch (Exception ex)
                {
                    steps?.Report(new QaProgressEvent { PlanId = planId, Step = stepName, State = QaStepState.Failed, IsCalculation = true, Duration = sw.Elapsed, Detail = ex.Message });
                    throw;
                }
                finally { trace.LogSummary(); }
                var warnings = trace.Issues;
                steps?.Report(new QaProgressEvent
                {
                    PlanId = planId, Step = stepName, State = res.Success ? QaStepState.Done : QaStepState.Failed, IsCalculation = true,
                    Model = model, Beams = beams.Count, ControlPoints = controlPoints, Duration = sw.Elapsed,
                    Detail = res.Success ? calcDetail : "Eclipse reported that the calculation failed",
                    Warnings = warnings.Count > 0 ? warnings : null,
                });
                foreach (var w in warnings) { _log($"{planId}: {w}"); if (!notes.Contains(w)) notes.Add(w); }
                if (!res.Success) throw new InvalidOperationException($"Eclipse reported that the dose calculation of '{planId}' failed.");
                _log($"{planId}: calculated in {sw.Elapsed.TotalSeconds:F0} s (pass {pass}, {model}, {beams.Count} beams, {controlPoints} CPs).");

                var diffs = beams.Select(b => (b.Id, qa: b.Meterset.Value, clin: clinicalMu[b.Id].Value)).ToList();
                double worst = diffs.Max(d => double.IsNaN(d.qa) ? double.PositiveInfinity : Math.Abs(d.qa - d.clin));
                bool ok = worst <= MuTolerance;
                steps?.Report(new QaProgressEvent
                {
                    PlanId = planId, Step = "Check MUs", State = ok || pass == 1 ? QaStepState.Done : QaStepState.Failed,
                    Detail = ok ? $"equal to the clinical MUs (max difference {worst:0.000} MU)"
                                : pass == 1 ? $"off by up to {worst:0.000} MU; correcting the weights" : $"still off by up to {worst:0.000} MU",
                    MuResults = diffs.Select(d => new BeamMuResult(d.Id, d.qa, d.clin, MuTolerance)).ToList(),
                });
                if (ok)
                {
                    notes.Add($"Calculated in {sw.Elapsed.TotalMinutes:0.0} min. MUs equal the clinical MUs (max difference {worst:0.000} MU): " +
                              string.Join(", ", diffs.Select(d => $"{d.Id} {d.qa:0.00}")) + ".");
                    return notes;
                }
                if (pass == 2)
                    throw new InvalidOperationException($"'{planId}': the calculated MUs don't match the clinical MUs after correction: " +
                        string.Join(", ", diffs.Select(d => $"{d.Id} {d.qa:0.00} vs {d.clin:0.00}")) + ".");
                _log($"{planId}: MUs off by up to {worst:0.000}; correcting weights.");
                foreach (var b in beams)
                    if (b.Meterset.Value > 0) SetWeight(b, b.WeightFactor * clinicalMu[b.Id].Value / b.Meterset.Value);
            }
            return notes;
        }

        /// <summary>
        /// Turns Eclipse's trace output during a calculation into progress events (throttled) and writes every line to
        /// the log, so the format can be checked (whether dose calculation reports progress this way is unconfirmed).
        /// </summary>
        private sealed class CalculationTrace
        {
            private readonly string _planId, _step;
            private readonly IProgress<QaProgressEvent>? _steps;
            private readonly Action<string> _log;
            private readonly object _lock = new object();
            private readonly List<CalculationIssue> _issues = new List<CalculationIssue>();
            private int _lines, _progressLines, _infoLines;
            private double _lastPercent = -1;
            private string? _lastMessage;
            private DateTime _lastReport = DateTime.MinValue;

            public CalculationTrace(string planId, string step, IProgress<QaProgressEvent>? steps, Action<string> log)
            {
                _planId = planId; _step = step; _steps = steps; _log = log;
            }

            /// <summary>Eclipse's warnings and errors for this calculation, one line per distinct message.</summary>
            public IReadOnlyList<string> Issues { get { lock (_lock) return EclipseTrace.Summarise(_issues); } }

            public void OnLine(string line)
            {
                if (EclipseTrace.IsNoise(line)) return;
                double? pct = EclipseTrace.ProgressPercent(line);
                var issue = pct.HasValue ? null : EclipseTrace.ParseIssue(line);
                // A "Progress N%" line is shown as the percentage, not repeated as a message.
                string? msg = pct.HasValue ? null : EclipseTrace.DisplayMessage(line);
                bool report;
                lock (_lock)
                {
                    _lines++;
                    if (issue != null) _issues.Add(issue);
                    // Log progress every 10 %, warnings/errors and other messages; count the per-beam "Information" lines only.
                    if (pct.HasValue) { _progressLines++; if (pct.Value % 10 == 0) _log($"Eclipse ({_planId}): {line.Trim()}"); }
                    else if (issue == null && msg == null) _infoLines++;
                    else if (issue == null) _log($"Eclipse ({_planId}): {line.Trim()}");

                    var now = DateTime.UtcNow;
                    bool percentMoved = pct.HasValue && Math.Abs(pct.Value - _lastPercent) >= 1;
                    bool messageChanged = msg != null && issue == null && msg != _lastMessage && (now - _lastReport).TotalSeconds >= 0.5;
                    report = percentMoved || messageChanged;
                    if (pct.HasValue) _lastPercent = pct.Value;
                    if (msg != null && issue == null) _lastMessage = msg;
                    if (report) _lastReport = now;
                }
                if (report)
                    _steps?.Report(new QaProgressEvent
                    {
                        PlanId = _planId, Step = _step, State = QaStepState.Running, IsCalculation = true,
                        EclipsePercent = pct, EclipseMessage = issue == null ? msg : null,
                    });
            }

            public void LogSummary()
            {
                lock (_lock)
                    _log(_lines == 0
                        ? $"{_planId}: Eclipse wrote no trace output during the calculation (progress was estimated)."
                        : $"{_planId}: Eclipse wrote {_lines} trace line(s): {_progressLines} progress, {_issues.Count} warning/error, {_infoLines} information (not logged).");
            }
        }

        private static void SetWeight(Beam b, double weight)
        {
            var ep = b.GetEditableParameters();
            ep.WeightFactor = weight;
            b.ApplyParameters(ep);
        }

        private Core.Geometry.DoseGrid ExtractQaDose(ExternalPlanSetup qa, Box3 region, IProgress<string>? progress, CancellationToken ct)
        {
            try { qa.DoseValuePresentation = DoseValuePresentation.Absolute; }
            catch (Exception ex) { _log("Could not switch QA plan to absolute dose: " + ex.Message); }
            var notes = new List<Core.Services.ValidationMessage>();
            return EsapiExtraction.ExtractDose(qa.Dose, region, ToGy(qa.DosePerFraction), progress, ct, notes);
        }

        // ------------------------------------------------------------------ move isocentre
        public Task<QaPlanResult> MoveIsocentreAsync(string courseId, string planId, Vec3 newIsocentre, Box3 doseRegion,
            IProgress<string>? progress = null, CancellationToken ct = default, IProgress<QaProgressEvent>? steps = null) =>
            _worker.RunAsync($"move the isocentre of '{planId}'", app =>
            {
                RequireModifiable();
                var clinical = RequireClinicalPlan();
                var course = RequireCourse(courseId);
                var qa = RequireTaggedPlan(course, planId);
                QaPlanTag.TryParse(qa.Comment, out var tag);
                ct.ThrowIfCancellationRequested();
                ct = CancellationToken.None; // from here on, finish this plan (see BuildQaPlan)

                progress?.Report($"{planId}: moving the isocentre to {newIsocentre}...");
                var iso = new VVector(newIsocentre.X, newIsocentre.Y, newIsocentre.Z);
                QaSteps.Run(steps, planId, "Move isocentre", () =>
                {
                    foreach (var b in qa.Beams.ToList())
                    {
                        var ep = b.GetEditableParameters();
                        ep.Isocenter = iso;
                        b.ApplyParameters(ep);
                    }
                    var moved = qa.Beams.Where(b => !b.IsSetupField).Select(b => b.IsocenterPosition.ToVec3()).ToList();
                    if (moved.Any(m => !m.ApproxEquals(newIsocentre, 0.01)))
                        throw new InvalidOperationException($"'{planId}': not all beams are at the new isocentre.");
                }, $"to {newIsocentre}");
                QaSteps.Run(steps, planId, "Save", () => SaveIfAuto(app, $"moving the isocentre of '{planId}'"));

                var messages = CalculateWithClinicalMus(clinical, qa, planId, progress, ct, steps).ToList();
                var dose = QaSteps.Run(steps, planId, "Read calculated dose", () => ExtractQaDose(qa, doseRegion, progress, ct));
                QaSteps.Run(steps, planId, "Save", () => SaveIfAuto(app, $"recalculating '{planId}'"));
                return new QaPlanResult
                {
                    CourseId = course.Id, PlanId = qa.Id, Orientation = tag?.Orientation ?? ArrayOrientation.Deg0, Isocentre = newIsocentre,
                    CalculationSucceeded = true, Messages = messages, Dose = dose,
                };
            });

        // ------------------------------------------------------------------ delete
        public Task DeleteQaPlansAsync(string courseId, IReadOnlyCollection<string> planIds, IProgress<string>? progress = null, CancellationToken ct = default) =>
            _worker.RunAsync($"delete QA plans in '{courseId}'", app =>
            {
                RequireModifiable();
                var course = RequireCourse(courseId);
                foreach (var id in planIds)
                {
                    ct.ThrowIfCancellationRequested();
                    var plan = course.ExternalPlanSetups.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (plan == null) { progress?.Report($"{id}: not found (already deleted?)."); continue; }
                    if (!QaPlanTag.TryParse(plan.Comment, out _))
                        throw new InvalidOperationException($"Plan '{id}' was not created by this app; it is not deleted.");
                    if (!course.CanRemovePlanSetup(plan)) throw new InvalidOperationException($"Eclipse doesn't allow deleting '{id}' (e.g. approved or has treatment records).");
                    course.RemovePlanSetup(plan); // don't touch the plan object afterwards (it is disposed)
                    progress?.Report($"Deleted {id}.");
                    _log($"Deleted QA plan {courseId}/{id}.");
                }
                SaveIfAuto(app, "deleting QA plans");
                return true;
            });
    }
}
