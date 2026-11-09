# SRS MapCHECK PSQA Plan Creator (Eclipse ESAPI v18)

Creates patient-specific QA plans for single-isocentre SRS plans, measured with a Sun Nuclear SRS MapCHECK in a StereoPHAN. It recommends the minimum number of QA plans and the array position for each, creates and calculates the plans in Eclipse, and supports iterative fine-tuning of the array position.

## Status

| Phase | Content | State |
|---|---|---|
| 0 | Spike: test the risky ESAPI behaviour on Citrix | **Done**: all questions answered on Citrix (2026-11-09) |
| 1 | Core: geometry, detector model, scoring, optimisation (+ unit tests) | **Done**: 84 tests passing |
| 2 | App shell on fake data | **Done**: full UI flow on demo data ([try it](docs/PHASE2_UI_CHECKLIST.md)) |
| 3 | ESAPI read path | **Done**: tested on Citrix (dose and PTVs match Eclipse) |
| 4 | ESAPI write path | **Done, to test on Citrix** ([checklist](docs/PHASE4_CHECKLIST.md)) |
| 5 | Hardening, deployment docs | not started |

## Repository layout (so far)

```
Directory.Build.props              shared settings; EsapiPath (override in EsapiPath.user.props)
SrsMcPsqa.sln                      Core, Fake, Esapi, App, tests
src/SrsMcPsqa.Core/                netstandard2.0, no ESAPI/WPF. Units mm and Gy, DICOM patient coordinates
  Geometry/                        Vec3, ArrayOrientation/PlaneAxes, IsoGeometry + ShiftLabels (sign conventions),
                                   DoseGrid, StructureMask, ContourRasterizer (even-odd)
  Detector/DiodeArray.cs           1,013-diode SRS MapCHECK lattice
  Prescription/                    PTV-ID prescription parser and resolver
  Optimisation/                    Scoring (J_k), PlanningModel (summed-area tables), CandidateSearch,
                                   SetCoverSolver, Recommender, ConstrainedOptimizer, FineTuneOptimizer
  Rendering/                       plane sampling and marching-squares isolines for the 2D view
  Models/                          DTOs, QA plan naming + Comment tag, request builders
  Services/IPlanningService.cs     draft UI <-> Eclipse contract (revised after spike results)
src/SrsMcPsqa.Fake/                netstandard2.0 synthetic patient + simulated IPlanningService (--fake)
src/SrsMcPsqa.Esapi/               net48 x64: ESAPI IPlanningService (EsapiWorker STA thread, extraction, validation)
src/SrsMcPsqa.Launcher/            read-only single-file Eclipse plugin that starts SrsMcPsqa.exe
src/SrsMcPsqa.App/                 net48 x64 WPF: WPF-UI 3.0.5, CommunityToolkit.Mvvm 8.2.2, HelixToolkit.Wpf 2.25.0
  ViewModels/                      shell, patient & plan, PTVs, planning, review & fine-tune, settings
  Views/, MainWindow.xaml          Fluent UI; left navigation; progress panel
  Controls/PlaneView.cs            2D array-plane renderer (dose, PTVs, 100% isodose, array, diodes, rulers, drag)
  Controls/Scene3DView.cs          3D overview (HelixToolkit)
  Infrastructure/                  settings (JSON, department + per-user), rolling log, dialogs, converters
tests/SrsMcPsqa.Core.Tests/        net8.0 xUnit, synthetic PTVs with analytic dose; end-to-end workflow on the fake service
tools/EsapiStubs/                  compile-check stand-ins for the ESAPI DLLs (from the v18 probe; never deployed)
deploy/                            per-environment department settings (test / production)
spike/
  SrsMcPsqa.Spike.sln
  SrsMcPsqa.Spike/                 net48 x64 WPF (WPF-UI 3.0.5), write-enabled ESAPI standalone
    EsapiWorker.cs                 all ESAPI calls on one dedicated STA thread
    SpikeRunner.cs                 steps 0-3b, cleanup
    BeamCopier.cs                  beam copy at a new isocentre (prototype for Phase 4)
    Reflect.cs                     reflection helpers for VERIFY items
  SrsMcPsqa.Spike.Launcher/        read-only single-file plugin that starts the exe
  SPIKE_CHECKLIST.md               what to run on Citrix and what to report back
```

## Build

Core and tests (any OS with the .NET 8 SDK, no ESAPI needed):

```bash
dotnet test tests/SrsMcPsqa.Core.Tests/SrsMcPsqa.Core.Tests.csproj
```

The app (Windows, .NET SDK 6+ or VS 2022). It references ESAPI, so it needs the v18 DLLs (`EsapiPath.user.props`) to build, even for demo data:

```bash
dotnet build SrsMcPsqa.sln -c Release
```

```bash
src/SrsMcPsqa.App/bin/Release/net48/SrsMcPsqa.exe --fake
```

Without `--fake` the app connects to Eclipse. On a machine without Eclipse (CI, Linux), compile against the stubs:

```bash
dotnet build tools/EsapiStubs/API/VMS.TPS.Common.Model.API.csproj -c Release
```

```bash
dotnet build SrsMcPsqa.sln -p:EsapiPath=$PWD/tools/EsapiStubs/out
```

The spike needs Windows, Visual Studio 2022 or the .NET SDK (6+), and the ESAPI v18 DLLs. See [spike/SPIKE_CHECKLIST.md](spike/SPIKE_CHECKLIST.md#a-build-windows-dev-machine-with-visual-studio-2022-or-the-net-sdk).

## VERIFY items

Status after the first Citrix run (ESAPI 18.1.0.472, API assembly 1.0.700.247). The probe confirmed every member below exists with the signature the code uses.

| # | Item | Result |
|---|---|---|
| V1 | Default `EsapiPath` for v18 | ESAPI DLLs are in the GAC on the Citrix server; set `EsapiPath.user.props` on the dev PC. |
| V2 | `CopyImageFromOtherPatient` | ✓ tested: ~18 s, new study, user origin kept, BODY with HU 250 comes across. Returns `StructureSet`; there's also an overload taking a target `Study`, and `CanCopyImageFromOtherPatient(...)`. **Whether structures come across is still untested (needs approval).** Template `SRSMC_0_AXB` has one structure, BODY with assigned HU 250. |
| V3 | `BeginModifications` | ✓ works once approved (2026-11-09). With the patient open in Eclipse: to confirm. |
| V4 | `AddExternalPlanSetupAsVerificationPlan` | ✓ creates an empty plan (no beams), links `VerifiedPlan`, **inherits the calculation model** (AcurosXB_1612); ~2 s. |
| V5 | `PlanSetup.Comment` writable | ✓ `{ get; set; }` |
| V6 | `ExternalBeamMachineParameters` | ✓ ctor (machine, energy, doseRate, technique, fluence) plus a settable `MLCId`. |
| V7 | `Add*Beam`, control-point editing | ✓ `AddVMATBeam` + per-control-point jaws/leaves reproduce a 180-CP SRS ARC (6X-FFF, 1400 MU/min, MLC 1934) with identical meterset weights; beam ID settable. |
| V8 | Calculation model / options | `GetCalculationModel(PhotonVolumeDose)` returns **empty** for the clinical plans; use `PhotonCalculationModel`, else keep the model the verification plan inherits. **Open:** dose calculation on the phantom failed: "material assigned to structure BODY has not been defined in the physical material table selected for the structure set" (see Phase 4 notes). |
| V9 | `CalculateDoseWithPresetValues` | **Preset MUs are ignored** on v18 verification plans: MU = weight × dose/fraction × MU/Gy, with MU/Gy = 100 (no reference point). ✓ Verified workaround: beam weights = clinical MU ÷ (dose/fraction × 100). Four arcs gave exactly the clinical MUs, on creation and after an isocentre move. The app checks every beam after each calculation. Calculation ~10 min per 4-arc plan (AcurosXB, 1 mm, whole phantom). Exists; `CalculationResult` has **only `Success`**, with no messages, so the app reports beam calculation logs (`Beam.CalculationLogs`) instead. |
| V10 | `BeamParameters.Isocenter` setter | ✓ moving the isocentre via `GetEditableParameters`/`ApplyParameters` works (tested). |
| V11 | `Course.RemovePlanSetup` | ✓ tested, plus `CanRemovePlanSetup`. Don't touch the plan object after removal. |
| V12 | Dose units | ✓ `DoseValuePresentation = Absolute` works even without BeginModifications. Previously: Test plan is `DoseValuePresentation=Relative`, so `VoxelToDoseValue` returns %. Must switch to Absolute (the spike now tests whether that works read-only). `GetVoxels` takes ~190 ms per plane at 468×241, so extraction must read only the z-planes around the PTVs. |
| V13 | `TotalDose`, `RTPrescription` | ✓ `TotalDose` (30 Gy); `RTPrescription` null for the test plan. |
| V14 | Launcher | ✓ Passes patient, course and plan to the exe. `Assembly.Location` is **empty** for a single-file plugin, so the config file next to the script can't work; use the constant path or the environment variable. |
| V15 | ESAPI on a dedicated STA thread | ✓ `CreateApplication` in 1.7 s, calls fine. |
| V16 | WPF-UI on Citrix | Renders (to be confirmed visually). |

## Phase 1 design notes and decisions to review

These are choices I made where the brief left room. Each is easy to change.

1. **Prescription parser.** PTV names only ever carry the total dose (confirmed), so there is no per-fraction rule. Department convention (confirmed): `PTV30` = 30 Gy, so a two-digit number straight after "PTV" is read as Gy. A single digit (`PTV1`) is a PTV number. Parsed values outside 1–100 Gy are rejected. Planning helpers (`z*` and `*eval*` by default; `HelperPatterns` setting) are listed but unticked.
2. **Coverage and the diode rule.** If no position near a PTV's best plane puts N_min diodes inside it (a very small PTV), the diode rule is waived for that PTV and it is reported as **LowDiodeCount**. Confirmed acceptable: the plane still intersects the PTV, and PTVs below ~1 cm equivalent diameter aren't treated.
3. **"Partial coverage".** A PTV is partial when, on its primary plane, more than 1% of its cross-section lies outside the usable square (e.g. a PTV bigger than the array). The tolerance is configurable (`ClipTolerance`).
4. **Ties are centred.** Once covered PTVs are fully inside the usable square, many positions score the same. The optimiser then centres the **intersected cross-sections** at that depth in the array (not the PTVs' 3D bounding boxes, which can be far off for irregular PTVs). The planning screen also has a **Centre on PTVs** button.
5. **Diode counts in the sweep.** These are expensive, so the diode rule is checked on the best candidate for each (covered set, orientation) rather than on every candidate. That candidate's set shrinks if the rule fails.
6. **Constrained re-optimisation.** Planes with assigned PTVs are optimised first, then the rest are covered by the free planes. Plans that are still unused get the plane adding the most to Σ max J_k, or else the best plane of the lowest-scoring PTV, so the requested plan count is honoured. If an assignment is impossible, the minimum J_k (not normalised) is maximised and a warning is given.
7. **Fine-tune constraint.** "Keep assigned PTVs inside the usable square" means no PTV may end up more clipped than it is now (or more than the clip tolerance, if that's larger), and PTVs on the plane must stay on it. If scores are equal, the smallest move is preferred, so an already-optimal plan gets Δ = 0.
8. **Manual edits.** The area and score terms use the nearest 0.5 mm depth (the cached tables). Diode counts use the exact plane.
9. **Limit.** At most 64 PTVs per model. Selected PTVs are passed in as the model's targets.

## Phase 2 notes

- **No JSON library.** Settings use `DataContractJsonSerializer` from .NET Framework, which avoids an assembly that could clash with what ESAPI loads.
- **Settings files.** Department defaults live in `SrsMcPsqa.settings.json` next to the exe (read-only, optional), one per installation; see [deploy/README.md](deploy/README.md). Personal preferences are in `%APPDATA%\SrsMcPsqa\settings.json`. Template phantom IDs entered in Settings are saved **per installation folder** (`%APPDATA%\SrsMcPsqa\template-<hash>.json`), because test and production use different template patients and `%APPDATA%` may follow you between them. "Reset to department defaults" deletes both personal files.
- **Errors.** Every error opens a dialog with **Copy details** and is logged to `%LOCALAPPDATA%\SrsMcPsqa\logs` (30 days kept).
- **Navigation.** It's a simple list rather than WPF-UI's `NavigationView`, which needs a page service. Screens unlock as the workflow progresses.
- **Review screen.** Each QA plan has its own tab. Values from before calculation and from fine-tune predictions are labelled as approximations. Calculated values are labelled "CALCULATED".
- **Array plane view.** u runs to the right, v (superior) up. At 0° patient left is on the right (seen from anterior). At 270° patient posterior is on the right.

## Phase 3 notes

- **Reading needs no approval.** The app opens the patient and tries `BeginModifications`. If Eclipse refuses (not approved, or the patient is locked), it carries on read-only with a yellow note, and creating QA plans comes later.
- **PTV masks.** Contours are read per CT plane (`GetContoursOnImagePlane`) and rasterised at 0.5 mm in-plane and the CT slice spacing in z, with the even-odd rule. Meshes come from `Structure.MeshGeometry` (3D only).
- **Dose.** The app switches the plan to absolute dose if it can. Otherwise it converts % with the plan total dose (100% = total dose) and says so. Voxel→Gy is calibrated from `VoxelToDoseValue` at three points. Only the z-planes within the PTVs + 45 mm are read: `GetVoxels` always reads a whole plane, about 0.2 s each.
- **Validation blocks the plan** (instead of just warning) for: no valid dose, more than one isocentre, non-HFS, non-photon beams, or no non-empty PTV.
- **Template check.** **Settings → Validate** closes your patient, opens the template read-only and re-opens your patient. ESAPI allows only one open patient.
- **Members not in the probe.** The ESAPI code also relies on `VVector.x/y/z`, `DoseValue.Dose`/`UnitAsString` and the standard enums. These weren't in the probe dump but are used the same way in the spike, which compiled on Citrix.

## Phase 4 notes (from the spike runs)

- **QA plan prescription.** 1 fraction at the clinical dose per fraction (confirmed). The clinical MUs are per fraction.
- **Phantom material.** Calculation needs the phantom BODY's material to be in the structure set's physical material table. This is a data setup item for the template phantom, fixed in Eclipse, not by the app.
- **MUs.** See V9: beam weights reproduce the clinical MUs; the app verifies every beam's MU after each calculation and stops with an error if any differs by more than 0.05 MU.
- **Beam order.** The QA plan keeps the clinical treatment order (`BeamsInTreatmentOrder` → `SetTreatmentOrder`, checked). Setup fields aren't copied.
- **Phantom reuse.** An image in the patient with the template image ID and a structure set with BODY is reused (the most recent if several). Each plan records the exact structure set by UID.
- **Template study lookup.** If a study ID is empty, the app saves, closes the patient, opens the template read-only to find the image's study, then re-opens the patient (cached per session).
- **Failures.** If anything fails after a plan was created, the incomplete plan is removed (or the message says to delete it).
- **Route.** Use the verification plan (`AddExternalPlanSetupAsVerificationPlan` + `AddVMATBeam` etc.). `Course.CopyPlanSetup` onto the phantom is refused: "Cannot copy to an image that is not registered nor has same frame of reference".
- **Calculation time.** About 10 min per 4-arc plan on Citrix, so the progress panel must say so, and fine-tune iterations are expensive.
- **Progress panel.** ESAPI gives no progress inside `CalculateDoseWithPresetValues` (it blocks until done), so the panel shows per-plan steps with their durations reported by the service (`QaProgressEvent`), and an *estimated* calculation bar from earlier calculations: seconds per control point per computer and model, a running average kept in `%LOCALAPPDATA%\SrsMcPsqa\calc-timing.json`. The first calculation with a model on a computer has no estimate. After each calculation it shows the per-beam MU check.
