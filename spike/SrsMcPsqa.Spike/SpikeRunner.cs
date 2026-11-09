using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;
using EsapiApplication = VMS.TPS.Common.Model.API.Application;

namespace SrsMcPsqa.Spike
{
    /// <summary>
    /// The Phase 0 spike steps. Every public method runs on the ESAPI thread (via <see cref="EsapiWorker"/>)
    /// and writes PASS/FAIL/INFO/WARN lines to the log. All state here is ESAPI-thread-only.
    /// </summary>
    internal sealed class SpikeRunner
    {
        /// <summary>Machine-readable tag written to the plan Comment. Only plans with this tag (and created this session) are deleted.</summary>
        public const string SpikeTag = "SRSMC-SPIKE|v0";
        private const string PlanIdPrefix = "SMCSPK";

        private readonly Action<string> _log;
        private Patient? _patient;
        private StructureSet? _phantomSs;
        private ExternalPlanSetup? _qaPlan;
        private readonly List<(string CourseId, string PlanId)> _createdPlans = new List<(string, string)>();

        public SpikeRunner(Action<string> log) => _log = log;

        private void Pass(string m) => _log("[PASS] " + m);
        private void Fail(string m) => _log("[FAIL] " + m);
        private void Info(string m) => _log("[INFO] " + m);
        private void Warn(string m) => _log("[WARN] " + m);
        private void Step(string m) => _log("\n==== " + m + " ====");

        private static string V(VVector v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";

        // ------------------------------------------------------------------------------------------------
        // Step 0: API probe (no patient needed). Resolves VERIFY items by reflection.
        // ------------------------------------------------------------------------------------------------
        public string ProbeApi(EsapiApplication app, string reportFolder)
        {
            Step("STEP 0 - ESAPI API probe");
            var asm = typeof(EsapiApplication).Assembly;
            Info($"ESAPI assembly: {asm.FullName}");
            Info($"ESAPI location: {asm.Location}");
            try { Info($"ESAPI file version: {FileVersionInfo.GetVersionInfo(asm.Location).FileVersion}"); } catch { /* informational */ }
            Info($"Current ESAPI user: {app.CurrentUser?.Id}");

            var checks = new (Type Type, string Member)[]
            {
                (typeof(Patient), "CanModifyData"), (typeof(Patient), "BeginModifications"),
                (typeof(Patient), "CopyImageFromOtherPatient"),
                (typeof(EsapiApplication), "SaveModifications"),
                (typeof(Course), "AddExternalPlanSetupAsVerificationPlan"), (typeof(Course), "CopyPlanSetup"),
                (typeof(Course), "RemovePlanSetup"), (typeof(Course), "CanRemovePlanSetup"),
                (typeof(PlanSetup), "Comment"), (typeof(PlanSetup), "Id"), (typeof(PlanSetup), "VerifiedPlan"),
                (typeof(PlanSetup), "RTPrescription"), (typeof(PlanSetup), "TotalDose"), (typeof(PlanSetup), "SetPrescription"),
                (typeof(PlanSetup), "GetCalculationModel"), (typeof(PlanSetup), "SetCalculationModel"),
                (typeof(PlanSetup), "GetCalculationOptions"), (typeof(PlanSetup), "SetCalculationOption"),
                (typeof(PlanSetup), "PhotonCalculationModel"), (typeof(PlanSetup), "PhotonCalculationOptions"),
                (typeof(PlanSetup), "DoseValuePresentation"),
                (typeof(ExternalPlanSetup), "CalculateDoseWithPresetValues"), (typeof(ExternalPlanSetup), "CalculateDose"),
                (typeof(ExternalPlanSetup), "AddVMATBeam"), (typeof(ExternalPlanSetup), "AddConformalArcBeam"),
                (typeof(ExternalPlanSetup), "AddMLCArcBeam"), (typeof(ExternalPlanSetup), "AddMLCBeam"),
                (typeof(ExternalPlanSetup), "AddArcBeam"), (typeof(ExternalPlanSetup), "AddStaticBeam"),
                (typeof(ExternalPlanSetup), "AddSlidingWindowBeam"), (typeof(ExternalPlanSetup), "AddMultipleStaticSegmentBeam"),
                (typeof(ExternalPlanSetup), "RemoveBeam"),
                (typeof(Beam), "GetEditableParameters"), (typeof(Beam), "ApplyParameters"), (typeof(Beam), "Id"),
                (typeof(BeamParameters), "Isocenter"), (typeof(BeamParameters), "ControlPoints"),
                (typeof(ControlPointParameters), "LeafPositions"), (typeof(ControlPointParameters), "JawPositions"),
                (typeof(ControlPointParameters), "MetersetWeight"),
                (typeof(ExternalBeamMachineParameters), ".ctor"), (typeof(ExternalBeamMachineParameters), "MLCId"),
                (typeof(Dose), "GetVoxels"), (typeof(Dose), "VoxelToDoseValue"),
                (typeof(Structure), "GetContoursOnImagePlane"), (typeof(Structure), "MeshGeometry"),
                (typeof(Structure), "AddContourOnImagePlane"), (typeof(Structure), "SetAssignedHU"), (typeof(Structure), "GetAssignedHU"),
                (typeof(StructureSet), "AddStructure"), (typeof(StructureSet), "CanAddStructure"),
                (typeof(Image), "UserOrigin"),
            };
            foreach (var (type, member) in checks)
            {
                var sigs = Reflect.Signatures(type, member);
                if (sigs.Count == 0) Warn($"{type.Name}.{member}: NOT FOUND");
                else foreach (var s in sigs) Info("  " + s);
            }

            // Full dump of key types to a separate file for later reference.
            var types = new[]
            {
                typeof(EsapiApplication), typeof(Patient), typeof(Study), typeof(Course), typeof(PlanSetup), typeof(ExternalPlanSetup),
                typeof(Beam), typeof(ControlPoint), typeof(BeamParameters), typeof(ControlPointParameters),
                typeof(ExternalBeamMachineParameters), typeof(CalculationResult), typeof(Image), typeof(StructureSet),
                typeof(Structure), typeof(Dose),
            };
            var path = Path.Combine(reportFolder, $"api-probe-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            File.WriteAllText(path, string.Join(Environment.NewLine, types.Select(Reflect.FullDump)));
            Pass($"Full API dump written to {path}");
            return path;
        }

        // ------------------------------------------------------------------------------------------------
        // Step 1: open patient + BeginModifications, describe the clinical plan (read path de-risking).
        // ------------------------------------------------------------------------------------------------
        public bool OpenPatient(EsapiApplication app, SpikeInputs inp, bool describePlan = true)
        {
            Step("STEP 1 - Open patient and BeginModifications");
            Info("Is the patient currently open in Eclipse? Note this in the checklist (the app can't tell).");
            if (string.IsNullOrWhiteSpace(inp.PatientId)) { Fail("No patient ID."); return false; }

            CloseCurrent(app);
            var sw = Stopwatch.StartNew();
            _patient = app.OpenPatientById(inp.PatientId);
            if (_patient == null) { Fail($"OpenPatientById('{inp.PatientId}') returned null."); return false; }
            Pass($"Opened patient '{_patient.Id}' in {sw.Elapsed.TotalSeconds:F1} s.");

            bool canModify = false;
            try { canModify = _patient.CanModifyData(); Info($"Patient.CanModifyData() = {canModify}"); }
            catch (Exception ex) { Fail("CanModifyData() threw: " + ex); }

            bool modifiable = true;
            try
            {
                _patient.BeginModifications();
                Pass("Patient.BeginModifications() succeeded.");
            }
            catch (Exception ex)
            {
                Fail("Patient.BeginModifications() threw:\n" + ex);
                Warn("Continuing READ-ONLY: the clinical plan is still described below. Steps 2, 3 and 3b need modifications and will not work.");
                modifiable = false;
            }

            // Read-only part: works whether or not modifications are allowed.
            foreach (var c in _patient.Courses)
                Info($"Course '{c.Id}': plans [{string.Join(", ", c.PlanSetups.Select(p => p.Id))}]");

            if (describePlan && !string.IsNullOrWhiteSpace(inp.ClinicalPlanId))
            {
                try { DescribeClinicalPlan(inp); }
                catch (Exception ex) { Fail("Describing clinical plan threw: " + ex); }
            }

            if (!modifiable) _patient = null; // later write steps then report "run step 1 first" instead of failing obscurely
            return modifiable;
        }

        private ExternalPlanSetup? FindClinicalPlan(SpikeInputs inp)
        {
            var course = _patient?.Courses.FirstOrDefault(c => c.Id == inp.ClinicalCourseId);
            if (course == null) { Fail($"Clinical course '{inp.ClinicalCourseId}' not found."); return null; }
            var plan = course.ExternalPlanSetups.FirstOrDefault(p => p.Id == inp.ClinicalPlanId);
            if (plan == null) Fail($"External plan '{inp.ClinicalPlanId}' not found in course '{course.Id}'.");
            return plan;
        }

        private void DescribeClinicalPlan(SpikeInputs inp)
        {
            Step("STEP 1b - Clinical plan read path");
            var plan = FindClinicalPlan(inp);
            if (plan == null) return;

            Info($"Plan '{plan.Id}' UID={plan.UID}");
            Info($"TreatmentOrientation={plan.TreatmentOrientation}; IsDoseValid={plan.IsDoseValid}; Dose present={plan.Dose != null}");
            Info($"Fractions={plan.NumberOfFractions}; DosePerFraction={plan.DosePerFraction}; TotalDose={plan.TotalDose}; " +
                 $"TreatmentPercentage={plan.TreatmentPercentage}; PlanNormalizationValue={plan.PlanNormalizationValue}");
            Info($"DoseValuePresentation={Reflect.TryGet(plan, "DoseValuePresentation")}");
            Info($"RTPrescription: {Reflect.TryGet(plan, "RTPrescription")?.ToString() ?? "null/not available"}");

            var model = plan.GetCalculationModel(CalculationType.PhotonVolumeDose);
            Info($"GetCalculationModel(PhotonVolumeDose) = '{model}'");
            Info($"PhotonCalculationModel property = '{plan.PhotonCalculationModel}'");
            foreach (var kv in plan.PhotonCalculationOptions ?? new Dictionary<string, string>())
                Info($"  PhotonCalculationOptions {kv.Key} = {kv.Value}");
            if (string.IsNullOrEmpty(model)) model = plan.PhotonCalculationModel;
            if (!string.IsNullOrEmpty(model))
                foreach (var kv in plan.GetCalculationOptions(model))
                    Info($"  option {kv.Key} = {kv.Value}");

            var beams = plan.Beams.ToList();
            foreach (var b in beams)
            {
                var cps = b.ControlPoints.ToList();
                Info($"Beam '{b.Id}': setup={b.IsSetupField}, machine={b.TreatmentUnit?.Id}, energy={b.EnergyModeDisplayName}, " +
                     $"doseRate={b.DoseRate}, technique={b.Technique?.Id}, MLCPlanType={b.MLCPlanType}, MLC={b.MLC?.Id ?? "none"}, " +
                     $"CPs={cps.Count}, gantry {cps.First().GantryAngle:F1}->{cps.Last().GantryAngle:F1} {b.GantryDirection}, " +
                     $"coll={cps.First().CollimatorAngle:F1}, couch={cps.First().PatientSupportAngle:F1}, MU={b.Meterset.Value:F2} {b.Meterset.Unit}, " +
                     $"iso={V(b.IsocenterPosition)}");
            }
            var tx = beams.Where(b => !b.IsSetupField).ToList();
            var isos = tx.Select(b => b.IsocenterPosition).ToList();
            bool singleIso = isos.All(i => (i - isos[0]).Length < 0.01);
            if (singleIso) Pass($"Single isocentre {V(isos[0])} for {tx.Count} treatment beams.");
            else Warn("Treatment beams do not share one isocentre.");

            // Dose comes back in % when the plan's presentation is Relative. Try switching to absolute (read-only allowed?).
            try
            {
                plan.DoseValuePresentation = DoseValuePresentation.Absolute;
                Pass($"DoseValuePresentation set to {plan.DoseValuePresentation} (without BeginModifications).");
            }
            catch (Exception ex) { Warn("Setting DoseValuePresentation = Absolute threw: " + ex.Message); }

            var dose = plan.Dose;
            if (dose != null)
            {
                Info($"Dose grid: size {dose.XSize}x{dose.YSize}x{dose.ZSize}, res {dose.XRes:F2}/{dose.YRes:F2}/{dose.ZRes:F2} mm, origin {V(dose.Origin)}, DoseMax3D={dose.DoseMax3D}");
                var sw = Stopwatch.StartNew();
                int z = dose.ZSize / 2;
                var buf = new int[dose.XSize, dose.YSize];
                dose.GetVoxels(z, buf);
                int max = int.MinValue;
                foreach (var v in buf) if (v > max) max = v;
                Pass($"Dose.GetVoxels(plane {z}) OK in {sw.ElapsedMilliseconds} ms; max raw={max} -> VoxelToDoseValue={dose.VoxelToDoseValue(max)}");
            }

            var ss = plan.StructureSet;
            var img = ss?.Image;
            if (ss != null && img != null)
            {
                Info($"CT '{img.Id}': {img.XSize}x{img.YSize}x{img.ZSize}, res {img.XRes:F2}/{img.YRes:F2}/{img.ZRes:F2}, orientation={img.ImagingOrientation}, origin={V(img.Origin)}, userOrigin={V(img.UserOrigin)}, HasUserOrigin={img.HasUserOrigin}");
                foreach (var s in ss.Structures.Where(s => s.Id.IndexOf("PTV", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    if (s.IsEmpty) { Info($"PTV '{s.Id}': empty"); continue; }
                    var sw = Stopwatch.StartNew();
                    int slices = 0, polys = 0, points = 0;
                    for (int k = 0; k < img.ZSize; k++)
                    {
                        var c = s.GetContoursOnImagePlane(k);
                        if (c.Length == 0) continue;
                        slices++; polys += c.Length; points += c.Sum(p => p.Length);
                    }
                    var bounds = s.MeshGeometry?.Bounds;
                    Info($"PTV '{s.Id}': {s.Volume:F2} cc, {slices} slices, {polys} polygons, {points} points (read in {sw.ElapsedMilliseconds} ms), mesh bounds={bounds}");
                }
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Step 2: copy the phantom image from the template patient; report what came across.
        // ------------------------------------------------------------------------------------------------
        public string? CopyPhantom(EsapiApplication app, SpikeInputs inp)
        {
            Step("STEP 2 - Copy phantom image from template patient");
            if (!RequirePatient()) return null;

            var imagesBefore = AllImages().Select(Key).ToList();
            var ssBefore = _patient!.StructureSets.Select(s => s.Id).ToList();

            var existing = AllImages().Where(i => i.Id == inp.TemplateImageId).ToList();
            foreach (var e in existing)
            {
                Info($"Patient already has image '{e.Id}' (study '{e.Series?.Study?.Id}'): {e.XSize}x{e.YSize}x{e.ZSize}, res {e.XRes:F2}/{e.YRes:F2}/{e.ZRes:F2}, userOrigin {V(e.UserOrigin)}");
            }
            if (existing.Count > 0 && !inp.ForceCopyImage)
            {
                var reuse = _patient.StructureSets.FirstOrDefault(s => s.Image != null && s.Image.Id == inp.TemplateImageId);
                if (reuse != null)
                {
                    _phantomSs = reuse;
                    Pass($"Reusing existing image and structure set '{reuse.Id}' (tick 'Force copy' to copy again).");
                    DescribeStructureSet(reuse);
                    return reuse.Id;
                }
                Warn("Image exists but has no structure set; copying again.");
            }

            var sw = Stopwatch.StartNew();
            object? result;
            try
            {
                // VERIFY: return type (expected StructureSet).
                result = _patient.CopyImageFromOtherPatient(inp.TemplatePatientId, inp.TemplateStudyId, inp.TemplateImageId);
            }
            catch (Exception ex)
            {
                Fail("CopyImageFromOtherPatient threw:\n" + ex);
                return null;
            }
            Pass($"CopyImageFromOtherPatient returned {result?.GetType().FullName ?? "null"} in {sw.Elapsed.TotalSeconds:F1} s.");

            var newImages = AllImages().Where(i => !imagesBefore.Contains(Key(i))).ToList();
            var newSs = _patient.StructureSets.Where(s => !ssBefore.Contains(s.Id)).ToList();
            Info($"New images: [{string.Join(", ", newImages.Select(Key))}]");
            Info($"New structure sets: [{string.Join(", ", newSs.Select(s => s.Id))}]");

            var ss = result as StructureSet ?? newSs.FirstOrDefault();
            if (ss == null) { Fail("Could not identify the copied image's structure set."); return null; }
            _phantomSs = ss;

            var img = ss.Image;
            if (img != null)
            {
                Info($"Phantom image '{img.Id}' study='{img.Series?.Study?.Id}' series='{img.Series?.Id}' FOR={img.FOR}");
                Info($"  size {img.XSize}x{img.YSize}x{img.ZSize}, res {img.XRes:F3}/{img.YRes:F3}/{img.ZRes:F3} mm, origin {V(img.Origin)}, orientation {img.ImagingOrientation}");
                Pass($"  UserOrigin (array centre, DICOM mm) = {V(img.UserOrigin)}");
            }
            DescribeStructureSet(ss);
            SaveIf(app, inp, "image copy");
            return ss.Id;
        }

        private void DescribeStructureSet(StructureSet ss)
        {
            Info($"Structure set '{ss.Id}' on image '{ss.Image?.Id}': {ss.Structures.Count()} structures");
            foreach (var s in ss.Structures)
            {
                string hu = s.GetAssignedHU(out double huValue) ? huValue.ToString("F0") : "none";
                Info($"  '{s.Id}' type={s.DicomType} empty={s.IsEmpty} vol={(s.IsEmpty ? 0 : s.Volume):F1} cc assignedHU={hu}");
            }
            bool hasBody = ss.Structures.Any(s => !s.IsEmpty && (s.DicomType == "EXTERNAL" || s.DicomType == "BODY"));
            if (hasBody) Pass("Structure set has a non-empty EXTERNAL/BODY structure.");
            else Warn("No non-empty EXTERNAL/BODY structure: dose calculation will probably fail. Structures may not have come across.");
            if (ss.Structures.Any(s => s.DicomType == "SUPPORT")) Info("Support (couch) structures present.");
        }

        // ------------------------------------------------------------------------------------------------
        // Step 2a: read the template patient (read-only), then re-open the target patient.
        // ------------------------------------------------------------------------------------------------
        public bool InspectTemplate(EsapiApplication app, SpikeInputs inp)
        {
            Step("STEP 2a - Inspect template patient (read-only), then re-open target");
            Warn("Closing the current patient. Unsaved modifications are discarded.");
            CloseCurrent(app);

            var tp = app.OpenPatientById(inp.TemplatePatientId);
            if (tp == null) { Fail($"Template patient '{inp.TemplatePatientId}' not found."); }
            else
            {
                Pass($"Opened template patient '{tp.Id}' (no BeginModifications).");
                var study = tp.Studies.FirstOrDefault(s => s.Id == inp.TemplateStudyId);
                if (study == null) Fail($"Study '{inp.TemplateStudyId}' not found. Studies: [{string.Join(", ", tp.Studies.Select(s => s.Id))}]");
                else
                {
                    var img = study.Images3D.FirstOrDefault(i => i.Id == inp.TemplateImageId);
                    if (img == null) Fail($"Image '{inp.TemplateImageId}' not found. Images: [{string.Join(", ", study.Images3D.Select(i => i.Id))}]");
                    else
                    {
                        Pass($"Template image '{img.Id}': {img.XSize}x{img.YSize}x{img.ZSize}, res {img.XRes:F3}/{img.YRes:F3}/{img.ZRes:F3}, origin {V(img.Origin)}, userOrigin {V(img.UserOrigin)}, HasUserOrigin={img.HasUserOrigin}, orientation={img.ImagingOrientation}");
                        foreach (var ss in tp.StructureSets.Where(s => s.Image != null && s.Image.Id == img.Id))
                        {
                            DescribeStructureSet(ss);
                            foreach (var s in ss.Structures.Where(s => !s.IsEmpty))
                            {
                                int slices = 0;
                                for (int k = 0; k < img.ZSize; k++) if (s.GetContoursOnImagePlane(k).Length > 0) slices++;
                                Info($"    '{s.Id}' contours on {slices} slices");
                            }
                        }
                    }
                }
                app.ClosePatient();
                Info("Template patient closed.");
            }
            return OpenPatient(app, inp, describePlan: false);
        }

        // ------------------------------------------------------------------------------------------------
        // Step 3: verification plan + beam copy at shifted isocentre + MU-preserving calc.
        // ------------------------------------------------------------------------------------------------
        public string? CreateQaPlan(EsapiApplication app, SpikeInputs inp)
        {
            Step("STEP 3 - Create verification plan, copy beam(s), calculate with preset MUs");
            if (!RequirePatient()) return null;
            var clinical = FindClinicalPlan(inp);
            if (clinical == null) return null;

            var qaCourse = _patient!.Courses.FirstOrDefault(c => c.Id == inp.QaCourseId);
            if (qaCourse == null) { Fail($"QA course '{inp.QaCourseId}' not found. Create it in Eclipse first."); return null; }

            var ss = _phantomSs;
            if (!string.IsNullOrWhiteSpace(inp.PhantomStructureSetId))
                ss = _patient.StructureSets.FirstOrDefault(s => s.Id == inp.PhantomStructureSetId) ?? ss;
            if (ss?.Image == null) { Fail("No phantom structure set/image. Run step 2 first or enter the structure set ID."); return null; }
            Info($"Phantom structure set '{ss.Id}', image '{ss.Image.Id}'");

            var tx = clinical.Beams.Where(b => !b.IsSetupField).ToList();
            if (tx.Count == 0) { Fail("Clinical plan has no treatment beams."); return null; }
            List<Beam> toCopy;
            if (!string.IsNullOrWhiteSpace(inp.BeamId))
            {
                var b = tx.FirstOrDefault(x => x.Id == inp.BeamId);
                if (b == null) { Fail($"Beam '{inp.BeamId}' not found among treatment beams."); return null; }
                toCopy = new List<Beam> { b };
            }
            else toCopy = inp.CopyAllBeams ? tx : new List<Beam> { tx[0] };

            var origin = ss.Image.UserOrigin;
            var iso = origin + new VVector(inp.IsoOffset1[0], inp.IsoOffset1[1], inp.IsoOffset1[2]);
            Info($"Phantom user origin {V(origin)}; QA isocentre = origin + ({SpikeInputs.FormatVector(inp.IsoOffset1)}) = {V(iso)}");

            ExternalPlanSetup qa;
            var sw = Stopwatch.StartNew();
            try
            {
                qa = qaCourse.AddExternalPlanSetupAsVerificationPlan(ss, clinical);
            }
            catch (Exception ex)
            {
                Fail("AddExternalPlanSetupAsVerificationPlan threw:\n" + ex);
                return null;
            }
            Pass($"AddExternalPlanSetupAsVerificationPlan created '{qa.Id}' in {sw.ElapsedMilliseconds} ms. Beams already present: {qa.Beams.Count()}");
            Info($"  VerifiedPlan = {(Reflect.TryGet(qa, "VerifiedPlan") as PlanSetup)?.Id ?? "(property missing or null)"}");
            Info($"  Initial calc model = '{qa.GetCalculationModel(CalculationType.PhotonVolumeDose)}'");
            _qaPlan = qa;
            _createdPlans.Add((qaCourse.Id, qa.Id));

            // Plan ID (13 chars max, unique in course).
            var existingIds = new HashSet<string>(qaCourse.PlanSetups.Where(p => p != qa).Select(p => p.Id));
            string id = Enumerable.Range(1, 99).Select(n => PlanIdPrefix + n).First(x => !existingIds.Contains(x));
            try { qa.Id = id; Pass($"Plan ID set to '{qa.Id}'."); }
            catch (Exception ex) { Fail($"Setting plan ID '{id}' threw: {ex.Message}"); }
            _createdPlans[_createdPlans.Count - 1] = (qaCourse.Id, qa.Id);

            // Comment tag. VERIFY: PlanSetup.Comment writable.
            var tag = $"{SpikeTag}|src={clinical.UID}|orient=spike";
            if (Reflect.TrySetProperty(qa, "Comment", tag, _log))
                Pass($"Plan Comment set; reads back '{Reflect.TryGet(qa, "Comment")}'.");

            // Calculation model and options. GetCalculationModel can return '' for the clinical plan (seen on v18):
            // fall back to PhotonCalculationModel, then to the model the verification plan inherited.
            try
            {
                string clinModel = clinical.GetCalculationModel(CalculationType.PhotonVolumeDose);
                Info($"Clinical GetCalculationModel = '{clinModel}', PhotonCalculationModel = '{clinical.PhotonCalculationModel}'");
                if (string.IsNullOrEmpty(clinModel)) clinModel = clinical.PhotonCalculationModel;
                string qaModel = qa.GetCalculationModel(CalculationType.PhotonVolumeDose);
                if (!string.IsNullOrEmpty(clinModel) && clinModel != qaModel)
                {
                    qa.SetCalculationModel(CalculationType.PhotonVolumeDose, clinModel);
                    Pass($"SetCalculationModel(PhotonVolumeDose, '{clinModel}') (was '{qaModel}').");
                }
                string model = string.IsNullOrEmpty(clinModel) ? qaModel : clinModel;
                Info($"QA plan model = '{qa.GetCalculationModel(CalculationType.PhotonVolumeDose)}'");

                var clinOpts = clinical.PhotonCalculationOptions ?? new Dictionary<string, string>();
                if (clinOpts.Count == 0 && !string.IsNullOrEmpty(model)) clinOpts = clinical.GetCalculationOptions(model);
                var qaOpts = qa.GetCalculationOptions(model);
                foreach (var kv in clinOpts)
                {
                    qaOpts.TryGetValue(kv.Key, out var current);
                    if (current == kv.Value) { Info($"  option {kv.Key} already '{kv.Value}'"); continue; }
                    bool ok = qa.SetCalculationOption(model, kv.Key, kv.Value);
                    (ok ? (Action<string>)Pass : Fail)($"  SetCalculationOption({kv.Key}: '{current}' -> '{kv.Value}') returned {ok}");
                }
            }
            catch (Exception ex) { Fail("Setting calculation model/options threw: " + ex); }

            if (inp.SetPrescription)
            {
                try
                {
                    qa.SetPrescription(clinical.NumberOfFractions ?? 1, clinical.DosePerFraction, clinical.TreatmentPercentage);
                    Pass($"SetPrescription({clinical.NumberOfFractions}, {clinical.DosePerFraction}, {clinical.TreatmentPercentage}).");
                }
                catch (Exception ex) { Fail("SetPrescription threw: " + ex.Message); }
            }

            // Beams.
            foreach (var b in toCopy)
            {
                try { BeamCopier.Copy(qa, b, iso, _log); }
                catch (Exception ex) { Fail($"Copying beam '{b.Id}' threw:\n{ex}"); return qa.Id; }
            }

            Calculate(qa, clinical);
            SaveIf(app, inp, "plan creation + calc");
            return qa.Id;
        }

        // ------------------------------------------------------------------------------------------------
        // Step 3b: move isocentre on the existing QA plan and recalculate.
        // ------------------------------------------------------------------------------------------------
        public bool MoveIso(EsapiApplication app, SpikeInputs inp)
        {
            Step("STEP 3b - Move isocentre and recalculate");
            if (!RequirePatient()) return false;
            if (_qaPlan == null) { Fail("No QA plan from step 3 in this session."); return false; }
            var clinical = FindClinicalPlan(inp);
            if (clinical == null) return false;

            var origin = _qaPlan.StructureSet.Image.UserOrigin;
            var iso = origin + new VVector(inp.IsoOffset2[0], inp.IsoOffset2[1], inp.IsoOffset2[2]);
            Info($"New QA isocentre = origin + ({SpikeInputs.FormatVector(inp.IsoOffset2)}) = {V(iso)}");

            foreach (var b in _qaPlan.Beams.Where(b => !b.IsSetupField).ToList())
            {
                var before = b.IsocenterPosition;
                var ep = b.GetEditableParameters();
                // VERIFY: BeamParameters.Isocenter setter in v18.
                if (Reflect.TrySetProperty(ep, "Isocenter", iso, _log))
                {
                    try
                    {
                        b.ApplyParameters(ep);
                        Pass($"Beam '{b.Id}': iso {V(before)} -> {V(b.IsocenterPosition)} via BeamParameters.Isocenter + ApplyParameters.");
                        continue;
                    }
                    catch (Exception ex) { Fail($"ApplyParameters with new isocentre threw: {ex.Message}"); }
                }

                Warn($"Beam '{b.Id}': falling back to remove + recreate at new isocentre.");
                var src = clinical.Beams.FirstOrDefault(x => x.Id == b.Id);
                if (src == null) { Fail($"Source beam '{b.Id}' not found in clinical plan; cannot recreate."); return false; }
                try
                {
                    _qaPlan.RemoveBeam(b);
                    BeamCopier.Copy(_qaPlan, src, iso, _log);
                }
                catch (Exception ex) { Fail("Recreate beam threw:\n" + ex); return false; }
            }

            Calculate(_qaPlan, clinical);
            SaveIf(app, inp, "iso move + recalc");
            return true;
        }

        private void Calculate(ExternalPlanSetup qa, ExternalPlanSetup clinical)
        {
            var ids = qa.Beams.Where(b => !b.IsSetupField).Select(b => b.Id).ToList();
            var presets = BeamCopier.PresetMus(clinical.Beams, ids);
            Info($"Preset MUs: {string.Join(", ", presets.Select(p => $"{p.Key}={p.Value.Value:F2} {p.Value.Unit}"))}");
            if (presets.Count != ids.Count) Warn("Not every QA beam ID matched a clinical beam ID.");

            Info("Calculating dose (cannot be cancelled)...");
            var sw = Stopwatch.StartNew();
            CalculationResult res;
            try { res = qa.CalculateDoseWithPresetValues(presets); }
            catch (Exception ex) { Fail("CalculateDoseWithPresetValues threw:\n" + ex); return; }
            (res.Success ? (Action<string>)Pass : Fail)($"CalculateDoseWithPresetValues: Success={res.Success} in {sw.Elapsed.TotalSeconds:F1} s");
            Info($"  CalculationResult: {Reflect.DescribeProperties(res)}");

            foreach (var b in qa.Beams.Where(b => !b.IsSetupField))
            {
                var c = clinical.Beams.FirstOrDefault(x => x.Id == b.Id);
                Info($"  Beam '{b.Id}': QA MU={b.Meterset.Value:F2}, clinical MU={c?.Meterset.Value:F2}, iso={V(b.IsocenterPosition)}");
            }
            var d = qa.Dose;
            if (d != null)
                Info($"  QA dose grid {d.XSize}x{d.YSize}x{d.ZSize}, res {d.XRes:F2}/{d.YRes:F2}/{d.ZRes:F2} (clinical {clinical.Dose?.XRes:F2}/{clinical.Dose?.YRes:F2}/{clinical.Dose?.ZRes:F2}); DoseMax3D={d.DoseMax3D}");
        }

        // ------------------------------------------------------------------------------------------------
        // Cleanup: remove the plans created in this session that carry the spike tag (or, if the comment
        // could not be written, were created in this session).
        // ------------------------------------------------------------------------------------------------
        public int DeleteSessionPlans(EsapiApplication app, SpikeInputs inp)
        {
            Step("CLEANUP - Delete spike QA plans (tagged SRSMC-SPIKE in the QA course, from any session)");
            if (!RequirePatient()) return 0;
            var course = _patient!.Courses.FirstOrDefault(c => c.Id == inp.QaCourseId);
            if (course == null) { Fail($"QA course '{inp.QaCourseId}' not found."); return 0; }
            int removed = 0;
            foreach (var plan in course.PlanSetups.ToList())
            {
                var comment = Reflect.TryGet(plan, "Comment") as string ?? "";
                if (!comment.StartsWith(SpikeTag)) continue;
                string id = plan.Id;
                try
                {
                    if (!course.CanRemovePlanSetup(plan)) { Fail($"CanRemovePlanSetup('{id}') = false."); continue; }
                    course.RemovePlanSetup(plan);
                    Pass($"RemovePlanSetup('{id}') succeeded.");
                    removed++;
                    if (_qaPlan != null && _qaPlan.Id == id) _qaPlan = null;
                }
                catch (Exception ex) { Fail($"RemovePlanSetup('{id}') threw: {ex.Message}"); }
            }
            _createdPlans.Clear();
            if (removed == 0) Info("No spike-tagged plans found in this course.");
            else SaveIf(app, inp, "delete");
            return removed;
        }

        public void Save(EsapiApplication app)
        {
            try { app.SaveModifications(); Pass("Application.SaveModifications() succeeded."); }
            catch (Exception ex) { Fail("SaveModifications threw:\n" + ex); }
        }

        public IReadOnlyList<string> CreatedPlanIds => _createdPlans.Select(p => p.PlanId).ToList();

        // ------------------------------------------------------------------------------------------------
        private void SaveIf(EsapiApplication app, SpikeInputs inp, string what)
        {
            if (!inp.SaveAfterWrite) { Info($"Not saving after {what} (auto-save unticked)."); return; }
            Info($"Saving after {what}...");
            Save(app);
        }

        private bool RequirePatient()
        {
            if (_patient != null) return true;
            Fail("No patient open with modifications. Run step 1 first.");
            return false;
        }

        private void CloseCurrent(EsapiApplication app)
        {
            app.ClosePatient();
            _patient = null;
            _phantomSs = null;
            _qaPlan = null;
        }

        private IEnumerable<Image> AllImages() => _patient!.Studies.SelectMany(s => s.Images3D);
        private static string Key(Image i) => (i.Series?.Study?.Id ?? "?") + "/" + i.Id;
    }
}
