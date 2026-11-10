using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Services;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using EsapiApplication = VMS.TPS.Common.Model.API.Application;

// Core DTOs are aliased explicitly: ESAPI has types with the same names (e.g. VMS.TPS.Common.Model.API.PatientSummary).
using PatientSummary = SrsMcPsqa.Core.Services.PatientSummary;
using CourseSummary = SrsMcPsqa.Core.Services.CourseSummary;
using PlanSummary = SrsMcPsqa.Core.Services.PlanSummary;
using ClinicalPlanData = SrsMcPsqa.Core.Services.ClinicalPlanData;
using ValidationMessage = SrsMcPsqa.Core.Services.ValidationMessage;
using ValidationSeverity = SrsMcPsqa.Core.Services.ValidationSeverity;
using PhantomImageInfo = SrsMcPsqa.Core.Services.PhantomImageInfo;
using QaPlanResult = SrsMcPsqa.Core.Services.QaPlanResult;
using QaPlanRequest = SrsMcPsqa.Core.Services.QaPlanRequest;
using TemplatePhantomSettings = SrsMcPsqa.Core.Services.TemplatePhantomSettings;
using Vec3 = SrsMcPsqa.Core.Geometry.Vec3;
using Box3 = SrsMcPsqa.Core.Geometry.Box3;
using DoseGrid = SrsMcPsqa.Core.Geometry.DoseGrid;
using GridGeometry = SrsMcPsqa.Core.Geometry.GridGeometry;
using ContourSlice = SrsMcPsqa.Core.Geometry.ContourSlice;
using Point2 = SrsMcPsqa.Core.Geometry.Point2;
using TriangleMesh = SrsMcPsqa.Core.Geometry.TriangleMesh;
using ArrayOrientation = SrsMcPsqa.Core.Geometry.ArrayOrientation;
using PtvInfo = SrsMcPsqa.Core.Models.PtvInfo;
using QaPlanTag = SrsMcPsqa.Core.Models.QaPlanTag;

namespace SrsMcPsqa.Esapi
{
    /// <summary>
    /// Eclipse implementation of <see cref="IPlanningService"/>. Phase 3: read path only. Every ESAPI call goes
    /// through <see cref="EsapiWorker"/>; only Core DTOs are returned. Fields below are touched on the ESAPI thread only.
    /// </summary>
    public sealed partial class EsapiPlanningService : IPlanningService, IDisposable
    {
        /// <summary>Dose crop margin around the PTVs, enough to slide a 77 mm array (section 4.3).</summary>
        public const double DoseMarginMm = 45.0;

        private readonly EsapiWorker _worker;
        private readonly Action<string> _log;

        private Patient? _patient;
        private string? _patientId;
        private bool _canModify;
        private string? _modifyError;
        private string? _clinicalCourseId;
        private string? _clinicalPlanId;

        public EsapiPlanningService(Action<string> log)
        {
            _log = log;
            _worker = new EsapiWorker(log);
        }

        // ------------------------------------------------------------------ patient
        public Task<PatientSummary> OpenPatientAsync(string patientId, CancellationToken ct = default) =>
            _worker.RunAsync($"open patient '{patientId}'", app =>
            {
                ct.ThrowIfCancellationRequested();
                Open(app, patientId);
                return Summarise();
            });

        private void Open(EsapiApplication app, string patientId)
        {
            app.ClosePatient();
            _patient = null; _patientId = null; _canModify = false; _modifyError = null;
            var sw = Stopwatch.StartNew();
            _patient = app.OpenPatientById(patientId) ?? throw new InvalidOperationException($"Patient '{patientId}' was not found.");
            _patientId = patientId;
            try
            {
                _patient.BeginModifications();
                _canModify = true;
            }
            catch (Exception ex)
            {
                // Typical causes: script not approved, user rights, patient locked. Reading still works.
                _modifyError = ex.Message;
                _log($"BeginModifications refused for {patientId}: {ex.Message}");
            }
            _log($"Opened patient {patientId} in {sw.Elapsed.TotalSeconds:F1} s; modifiable={_canModify}.");
        }

        private PatientSummary Summarise()
        {
            var p = RequirePatient();
            return new PatientSummary
            {
                Id = p.Id,
                DisplayName = p.Name,
                CanModify = _canModify,
                ModifyError = _modifyError,
                Courses = p.Courses.Select(c => new CourseSummary
                {
                    Id = c.Id,
                    IsCompleted = c.IsCompleted(),
                    Plans = c.PlanSetups.Select(pl => Summary(c, pl)).ToList(),
                }).ToList(),
            };
        }

        private static PlanSummary Summary(Course c, PlanSetup p)
        {
            QaPlanTag.TryParse(p.Comment, out var tag);
            return new PlanSummary { CourseId = c.Id, PlanId = p.Id, IsQaPlan = tag != null, Tag = tag };
        }

        private Patient RequirePatient() => _patient ?? throw new InvalidOperationException("No patient is open.");

        // ------------------------------------------------------------------ clinical plan
        public Task<ClinicalPlanData> LoadClinicalPlanAsync(string courseId, string planId, IProgress<string>? progress = null, CancellationToken ct = default) =>
            _worker.RunAsync($"read plan '{courseId}/{planId}'", app =>
            {
                var sw = Stopwatch.StartNew();
                var p = RequirePatient();
                var course = p.Courses.FirstOrDefault(c => c.Id == courseId) ?? throw new InvalidOperationException($"Course '{courseId}' not found.");
                var plan = course.ExternalPlanSetups.FirstOrDefault(x => x.Id == planId)
                           ?? throw new InvalidOperationException($"'{planId}' is not an external beam plan in course '{courseId}'.");

                progress?.Report("Checking the plan...");
                var validation = EsapiExtraction.Validate(plan);
                var totalGy = EsapiExtraction.TotalDoseGy(plan);
                _clinicalCourseId = courseId;
                _clinicalPlanId = planId;
                var data = new ClinicalPlanData
                {
                    CourseId = courseId, PlanId = planId, PlanUid = plan.UID, PlanTotalDoseGy = totalGy,
                    Fractions = plan.NumberOfFractions is int nf && nf > 0 ? nf : 1,
                    Beams = plan.Beams.Where(b => !b.IsSetupField).Select(b =>
                    {
                        var cps = b.ControlPoints.ToList();
                        return new Core.Collision.BeamGeometry(b.Id, cps.Count > 0 ? cps[0].PatientSupportAngle : 0, cps.Select(c => c.GantryAngle).ToList());
                    }).ToList(),
                    Validation = validation,
                };
                var iso = plan.Beams.FirstOrDefault(b => !b.IsSetupField)?.IsocenterPosition;
                if (iso != null) data.Isocentre = iso.Value.ToVec3();
                if (validation.Any(v => v.Severity == ValidationSeverity.Error)) return data;

                string model = SafeModel(plan);
                validation.Add(new ValidationMessage(ValidationSeverity.Info,
                    $"Total dose {(totalGy.HasValue ? totalGy.Value.ToString("0.##") + " Gy" : "unknown")}" +
                    (plan.NumberOfFractions is int n ? $" in {n} fraction(s)" : "") +
                    (model.Length > 0 ? $"; calculation model {model}." : ".")));

                ct.ThrowIfCancellationRequested();
                var ptvs = EsapiExtraction.ExtractPtvs(plan.StructureSet, totalGy, progress, ct, validation);
                data.Ptvs = ptvs;
                if (ptvs.Count == 0)
                {
                    validation.Add(new ValidationMessage(ValidationSeverity.Error, "No non-empty structures with 'PTV' in the ID."));
                    return data;
                }

                // Absolute dose, cropped to the PTVs + 45 mm.
                try
                {
                    if (plan.DoseValuePresentation != DoseValuePresentation.Absolute)
                        plan.DoseValuePresentation = DoseValuePresentation.Absolute;
                }
                catch (Exception ex)
                {
                    _log("Could not switch dose presentation to absolute (will convert from %): " + ex.Message);
                }
                var region = ptvs.Select(x => x.Mask!.Bounds).Aggregate(Box3.Union).Expand(DoseMarginMm);
                data.Dose = EsapiExtraction.ExtractDose(plan.Dose, region, totalGy, progress, ct, validation);
                _log($"Loaded plan {courseId}/{planId}: {ptvs.Count} PTVs, dose crop {data.Dose.Geometry.Nx}×{data.Dose.Geometry.Ny}×{data.Dose.Geometry.Nz}, {sw.Elapsed.TotalSeconds:F1} s.");
                return data;
            });

        private static string SafeModel(PlanSetup plan)
        {
            try
            {
                var m = plan.GetCalculationModel(CalculationType.PhotonVolumeDose);
                return string.IsNullOrEmpty(m) ? plan.PhotonCalculationModel ?? "" : m;
            }
            catch
            {
                return "";
            }
        }

        // ------------------------------------------------------------------ QA plans (read)
        public Task<IReadOnlyList<PlanSummary>> ListQaPlansAsync(string courseId, CancellationToken ct = default) =>
            _worker.RunAsync<IReadOnlyList<PlanSummary>>($"list QA plans in '{courseId}'", app =>
            {
                var course = RequirePatient().Courses.FirstOrDefault(c => c.Id == courseId);
                if (course == null) return new List<PlanSummary>();
                return course.PlanSetups.Select(p => Summary(course, p)).Where(s => s.IsQaPlan).ToList();
            });

        // ------------------------------------------------------------------ template phantom (read-only)
        /// <summary>
        /// Opens the template patient read-only (ESAPI allows one open patient, so the current patient is closed and
        /// re-opened afterwards) and checks both images (each in its own study, or searched across all studies).
        /// </summary>
        public Task<IReadOnlyList<ValidationMessage>> ValidateTemplateAsync(TemplatePhantomSettings settings, CancellationToken ct = default) =>
            _worker.RunAsync<IReadOnlyList<ValidationMessage>>($"check template patient '{settings.PatientId}'", app =>
            {
                var msgs = new List<ValidationMessage>();
                void Add(ValidationSeverity s, string t) => msgs.Add(new ValidationMessage(s, t));
                string? current = _patientId;
                app.ClosePatient();
                _patient = null;
                try
                {
                    if (string.IsNullOrWhiteSpace(settings.PatientId)) { Add(ValidationSeverity.Error, "Template patient ID is not set."); return msgs; }
                    var tp = app.OpenPatientById(settings.PatientId);
                    if (tp == null) { Add(ValidationSeverity.Error, $"Template patient '{settings.PatientId}' not found."); return msgs; }
                    Add(ValidationSeverity.Info, $"Template patient '{tp.Id}' found (opened read-only).");

                    if (!string.IsNullOrWhiteSpace(settings.Image0Id) && string.Equals(settings.Image0Id, settings.Image270Id, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(settings.Study0Id ?? "", settings.Study270Id ?? "", StringComparison.OrdinalIgnoreCase))
                        Add(ValidationSeverity.Error, $"The 0° and 270° image IDs are the same ('{settings.Image0Id}'); they must be different images.");
                    foreach (var o in new[] { ArrayOrientation.Deg0, ArrayOrientation.Deg270 })
                    {
                        string label = o == ArrayOrientation.Deg0 ? "0°" : "270°";
                        var (study, img, error) = EsapiExtraction.FindTemplateImage(tp, settings.StudyId(o), settings.ImageId(o));
                        if (img == null) { Add(ValidationSeverity.Error, $"{label}: {error}"); continue; }
                        Add(ValidationSeverity.Info, $"{label} image '{img.Id}' in study '{study!.Id}': {img.XSize}×{img.YSize}×{img.ZSize}, {img.XRes:0.###}/{img.YRes:0.###}/{img.ZRes:0.###} mm, user origin {img.UserOrigin.ToVec3()}.");
                        if (!img.HasUserOrigin)
                            Add(ValidationSeverity.Info, $"{label} image '{img.Id}' uses Eclipse's default user origin (DICOM origin {img.UserOrigin.ToVec3()}) as the array centre. " +
                                "Fine for the department template; for a new template, check this point is the array centre.");
                        if (img.ImagingOrientation != PatientOrientation.HeadFirstSupine)
                            Add(ValidationSeverity.Warning, $"{label} image orientation is {img.ImagingOrientation} (expected HFS).");
                        var ss = tp.StructureSets.FirstOrDefault(x => x.Image != null && x.Image.Equals(img));
                        if (ss == null) Add(ValidationSeverity.Warning, $"{label} image '{img.Id}' has no structure set.");
                        else if (!ss.Structures.Any(x => !x.IsEmpty && (x.DicomType == "EXTERNAL" || x.DicomType == "BODY")))
                            Add(ValidationSeverity.Warning, $"Structure set '{ss.Id}' has no BODY/EXTERNAL structure (needed for dose calculation).");
                    }
                    return msgs;
                }
                finally
                {
                    app.ClosePatient();
                    if (current != null)
                    {
                        try { Open(app, current); }
                        catch (Exception ex) { Add(ValidationSeverity.Error, $"Could not re-open patient '{current}': {ex.Message}"); }
                    }
                }
            });

        public bool IsBusy => _worker.IsBusy;

        public bool SupportsWriting => true;

        public void Dispose() => _worker.Dispose();
    }
}
