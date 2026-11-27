# Technical notes

Background for maintainers: what was verified about ESAPI v18.1, the design decisions behind the planning and fine-tune
algorithms, and implementation notes. For using and deploying the app, see the [README](../README.md).

## ESAPI behaviour (v18.1, verified on Citrix)

ESAPI 18.1.0.472 (API assembly 1.0.700.247). A probe confirmed every member below exists with the signature the code uses; the results come from test runs on the Citrix test system.

| # | Item | Result |
|---|---|---|
| V1 | Default `EsapiPath` for v18 | ESAPI DLLs are in the GAC on the Citrix server; set `EsapiPath.user.props` on the dev PC. |
| V2 | `CopyImageFromOtherPatient` | ✓ tested: ~18 s, new study, user origin kept, BODY with HU 250 comes across. Returns `StructureSet`; there's also an overload taking a target `Study`, and `CanCopyImageFromOtherPatient(...)`. **Whether structures come across is still untested (needs approval).** Template `SRSMC_0_AXB` has one structure, BODY with assigned HU 250. |
| V3 | `BeginModifications` | ✓ works once approved (2026-11-09), also with the patient open in Eclipse. |
| V4 | `AddExternalPlanSetupAsVerificationPlan` | ✓ creates an empty plan (no beams), links `VerifiedPlan`, **inherits the calculation model** (AcurosXB_1612); ~2 s. |
| V5 | `PlanSetup.Comment` writable | ✓ `{ get; set; }` |
| V6 | `ExternalBeamMachineParameters` | ✓ ctor (machine, energy, doseRate, technique, fluence) plus a settable `MLCId`. |
| V7 | `Add*Beam`, control-point editing | ✓ `AddVMATBeam` + per-control-point jaws/leaves reproduce a 180-CP SRS ARC (6X-FFF, 1400 MU/min, MLC 1934) with identical meterset weights; beam ID settable. |
| V8 | Calculation model / options | `GetCalculationModel(PhotonVolumeDose)` returns **empty** for the clinical plans; use `PhotonCalculationModel`, else keep the model the verification plan inherits. The earlier material-table error was a test-data issue, fixed in Eclipse; calculations now succeed. |
| V9 | `CalculateDoseWithPresetValues` | **Preset MUs are ignored** on v18 verification plans: MU = weight × dose/fraction × MU/Gy, with MU/Gy = 100 (no reference point). ✓ Verified workaround: beam weights = clinical MU ÷ (dose/fraction × 100). Four arcs gave exactly the clinical MUs, on creation and after an isocentre move. The app checks every beam after each calculation. Calculation ~10 min per 4-arc plan (AcurosXB, 1 mm, whole phantom). Exists; `CalculationResult` has **only `Success`**; progress (`Progress N%`, one 0–100 count per plan, ~25 % per beam) and the warnings/errors summary come from Eclipse's .NET trace output (`EclipseTraceListener`). |
| V10 | `BeamParameters.Isocenter` setter | ✓ moving the isocentre via `GetEditableParameters`/`ApplyParameters` works (tested). |
| V11 | `Course.RemovePlanSetup` | ✓ tested, plus `CanRemovePlanSetup`. Don't touch the plan object after removal. |
| V12 | Dose units | ✓ `DoseValuePresentation = Absolute` works even without BeginModifications. The test plan is `DoseValuePresentation=Relative`, so `VoxelToDoseValue` returns % until switched to Absolute. `GetVoxels` takes ~190 ms per plane at 468×241, so extraction must read only the z-planes around the PTVs. |
| V13 | `TotalDose`, `RTPrescription` | ✓ `TotalDose` (30 Gy); `RTPrescription` null for the test plan. |
| V14 | Launcher | ✓ Passes patient, course and plan to the exe. `Assembly.Location` is **empty** for a single-file plugin, so the config file next to the script can't work; use the constant path or the environment variable. |
| V15 | ESAPI on a dedicated STA thread | ✓ `CreateApplication` in 1.7 s, calls fine. |
| V16 | WPF-UI on Citrix | ✓ renders. |
| V17 | Calculation progress | ✓ `CalculateDoseWithPresetValues` writes `Progress N%` (one 0–100 count per plan) and a warnings summary to .NET trace output, read by `EclipseTraceListener`. |
| V18 | Cancel during a calculation | ✓ Cancel now acts between plans; a started plan is always calculated, read and saved. |
| V19 | `Patient.AddCourse` + `Course.Id` setter (create the QA course) | **To test** ([Phase 5 checklist](PHASE5_CHECKLIST.md)). |
| V20 | Finding earlier QA plans (tag, dose read without `BeginModifications`) | **To test**. |
| V21 | Collision check geometry (head face at 37.5 cm, 76.5 cm across; Exact IGRT couch profile) | Conventions match two open-source ESAPI collision scripts; couch rotation direction **to confirm on the machine** ([details](COLLISION_CHECK.md)). |
| V22 | Maximised window | ✓ maximise after load (WPF-UI pads only on a state change). |

## Design decisions

Choices made where the brief left room. Each is easy to change.

1. **Prescription parser.** PTV names only ever carry the total dose (confirmed), so there is no per-fraction rule. Department convention (confirmed): `PTV30` = 30 Gy, so a two-digit number straight after "PTV" is read as Gy. A single digit (`PTV1`) is a PTV number. Parsed values outside 1–100 Gy are rejected. Planning helpers (`z*` and `*eval*` by default; `HelperPatterns` setting) are listed but unticked.
2. **Coverage and the diode rule.** If no position near a PTV's best plane puts N_min diodes inside it (a very small PTV), the diode rule is waived for that PTV and it is reported as **LowDiodeCount**. Confirmed acceptable: the plane still intersects the PTV, and PTVs below ~1 cm equivalent diameter aren't treated.
3. **"Partial coverage".** A PTV is partial when, on its primary plane, more than 1% of its cross-section lies outside the usable square (e.g. a PTV bigger than the array). The tolerance is configurable (`ClipTolerance`).
4. **Ties are centred.** Once covered PTVs are fully inside the usable square, many positions score the same. The optimiser then centres the **intersected cross-sections** at that depth in the array (not the PTVs' 3D bounding boxes, which can be far off for irregular PTVs). The planning screen also has a **Centre on PTVs** button.
5. **Diode counts in the sweep.** These are expensive, so the diode rule is checked on the best candidate for each (covered set, orientation) rather than on every candidate. That candidate's set shrinks if the rule fails.
6. **Constrained re-optimisation.** Planes with assigned PTVs are optimised first, then the rest are covered by the free planes. Plans that are still unused get the plane adding the most to Σ max J_k, or else the best plane of the lowest-scoring PTV, so the requested plan count is honoured. If an assignment is impossible, the minimum J_k (not normalised) is maximised and a warning is given.
7. **Fine-tune constraint.** "Keep assigned PTVs inside the usable square" means no PTV may end up more clipped than it is now (or more than the clip tolerance, if that's larger), and PTVs on the plane must stay on it. If scores are equal, the smallest move is preferred, so an already-optimal plan gets Δ = 0.
8. **Manual edits.** The area and score terms use the nearest 0.5 mm depth (the cached tables). Diode counts use the exact plane.
9. **Limit.** At most 64 PTVs per model. Selected PTVs are passed in as the model's targets.

## App notes

- **No JSON library.** Settings use `DataContractJsonSerializer` from .NET Framework, which avoids an assembly that could clash with what ESAPI loads.
- **Settings files.** All settings except theme and log level are department settings in `SrsMcPsqa.settings.json` next to the exe, one per installation (test and production differ). Saving them from the app asks for confirmation, needs write access to the app folder and keeps a `.bak`. Theme and log level are personal (`%APPDATA%\SrsMcPsqa\settings.json`). See [deploy/README.md](../deploy/README.md).
- **Errors.** Every error opens a dialog with **Copy details** and is logged to `<app folder>\logs\srsmcpsqa_<user>_<computer>.log` (falls back to `%LOCALAPPDATA%\SrsMcPsqa\logs` if that folder can't be written).
- **Navigation.** It's a simple list rather than WPF-UI's `NavigationView`, which needs a page service. Screens unlock as the workflow progresses.
- **Review screen.** QA plans are listed on the left. Values from before calculation and fine-tune previews are labelled as approximations; calculated values are labelled "CALCULATED".
- **Array plane view.** u runs to the right, v (superior) up. At 0° patient left is on the right (seen from anterior). At 270° patient posterior is on the right.

## Reading from Eclipse

- **Reading needs no approval.** The app opens the patient and tries `BeginModifications`. If Eclipse refuses (not approved, or the patient is locked), it carries on read-only with a yellow note, and creating QA plans stays disabled.
- **PTV masks.** Contours are read per CT plane (`GetContoursOnImagePlane`) and rasterised at 0.5 mm in-plane and the CT slice spacing in z, with the even-odd rule. Meshes come from `Structure.MeshGeometry` (3D only).
- **Dose.** The app switches the plan to absolute dose if it can. Otherwise it converts % with the plan total dose (100% = total dose) and says so. Voxel→Gy is calibrated from `VoxelToDoseValue` at three points. Only the z-planes within the PTVs + 45 mm are read: `GetVoxels` always reads a whole plane, about 0.2 s each.
- **Validation blocks the plan** (instead of just warning) for: no valid dose, more than one isocentre, non-HFS, non-photon beams, or no non-empty PTV.
- **Template check.** **Settings → Validate** closes your patient, opens the template read-only and re-opens your patient. ESAPI allows only one open patient.
- **Members not in the probe.** The ESAPI code also relies on `VVector.x/y/z`, `DoseValue.Dose`/`UnitAsString` and the standard enums. These weren't in the probe dump but compiled and ran on Citrix (Phase 0 test tool, since removed).

## Writing to Eclipse

- **5×5 constancy field plan.** For each array orientation used, a verification plan of the clinical plan on that phantom image, tagged `|kind=ref5x5` (`QaPlanTag`), ID from `ReferencePlanIdPattern` (`ReferenceField.PlanId`, a letter added when taken). One `AddStaticBeam` per clinical energy (machine, energy/fluence mode and dose rate of the first clinical beam of that energy), jaws ±25 mm, MLC retracted, collimator 0°, couch 0°, gantry 0° (0° image) or 90° (270° image), isocentre at the user origin. Technique "STATIC". MUs: the same weight method and check as the QA plans, with 100 MU per field as the target (`CalculateWithMus`). Find existing flags it out of date if the energies, dose rates or MUs differ. No couch clearance check (fixed position, always clear).
- **Calculation model.** QA plans inherit the clinical plan's model (V4), and `MatchCalculationModel` copies its options. The department setting `TemplateCalculationModel` names the model the template phantom is set up for. QA plans are created or recalculated only when the clinical plan's model (and, for recalculation, the QA plan's) matches it, case-insensitively (`CalculationModelCheck`). The check runs in the UI (plan load message, Create and recalculation off) and again in the ESAPI service before any write (`RequirePhantomModel`). The Settings list comes from `ExternalPlanSetup.GetModelsForCalculationType(CalculationType.PhotonVolumeDose)` on the loaded clinical plan; the models are also logged when a plan loads.
- **Calculation warnings box.** After a calculation with warnings (e.g. "The calibration protocol status is not Commissioning or Approved", "The field exits the body in a region where the CT data ends"), Eclipse opens a plain Windows message box in the app's process and the calculation waits for OK. ESAPI has no documented way to turn it off. `EclipseWarningDialogCloser` watches for standard dialogs in the process (`SetWinEventHook`, `EVENT_SYSTEM_DIALOGSTART`), logs their text, and presses the button only for a single-button box whose every line starts with `Warning:` (`EclipseWarningDialog.IsWarningsOnly`). Any other dialog is left open and its text logged. The warnings are also captured from the trace and shown in the progress panel and on the plan's card.
- **Calling assembly (clinical system).** Before a modifying call (`AddCourse`, plan creation, calculation), Eclipse looks for the approved script assembly as the caller. It refused 1.0.0 and 1.0.1 with "The system did not find a calling assembly while script 'SrsMcPsqa' tried to modify data … NoInlining". In those builds every ESAPI call was made from `SrsMcPsqa.Esapi.dll`, on the dedicated ESAPI thread, so no frame of `SrsMcPsqa.exe` was on that thread's stack. Turning off optimisation for the DLL alone (1.0.1) didn't help. The ESAPI code is now compiled into the exe (`src/SrsMcPsqa.App/Esapi`), which is built without optimisation so inlining and tail calls can't remove the calling frame. The earlier test system accepted the DLL build, probably because it didn't enforce approvals; that's an inference, not something we confirmed.
- **QA plan prescription.** 1 fraction at the clinical dose per fraction (confirmed). The clinical MUs are per fraction.
- **Phantom material.** Calculation needs the phantom BODY's material to be in the structure set's physical material table. This is a data setup item for the template phantom, fixed in Eclipse, not by the app.
- **MUs.** See V9: beam weights reproduce the clinical MUs; the app verifies every beam's MU after each calculation and stops with an error if any differs by more than 0.05 MU.
- **Beam order.** The QA plan keeps the clinical treatment order (`BeamsInTreatmentOrder` → `SetTreatmentOrder`, checked). Setup fields aren't copied.
- **Phantom reuse.** An image in the patient with the template image ID and a structure set with BODY is reused (the most recent if several). Each plan records the exact structure set by UID.
- **Template study lookup.** If a study ID is empty, the app saves, closes the patient, opens the template read-only to find the image's study, then re-opens the patient (cached per session).
- **Failures.** If anything fails after a plan was created, the incomplete plan is removed (or the message says to delete it).
- **Route.** Use the verification plan (`AddExternalPlanSetupAsVerificationPlan` + `AddVMATBeam` etc.). `Course.CopyPlanSetup` onto the phantom is refused: "Cannot copy to an image that is not registered nor has same frame of reference".
- **Calculation time.** About 8–10 min per 4-arc plan on the Citrix test system (production is faster); fine-tune iterations each need a recalculation.
- **Progress panel.** ESAPI has no progress API for `CalculateDoseWithPresetValues` (it blocks until done). Eclipse does write progress text to .NET trace output during DVH estimation and optimisation (gatewayscripts.com, "Automated planning from templates IV: unlocking real-time feedback from Eclipse"), so the app attaches a `TraceListener` during each calculation, logs every line and shows "Eclipse: N%" when it sees `Progress … N%` lines (`EclipseTrace`). Confirmed on Citrix (v18.1, standalone exe): `CalculateDoseWithPresetValues` writes `Progress N%` lines. Otherwise the panel shows per-plan steps with their durations reported by the service (`QaProgressEvent`), and an *estimated* calculation bar from earlier calculations: seconds per control point per computer and model, a running average kept in `%LOCALAPPDATA%\SrsMcPsqa\calc-timing.json`. The first calculation with a model on a computer has no estimate. After each calculation it shows the per-beam MU check.
