# SRS MapCHECK PSQA Plan Creator (Eclipse ESAPI v18)

Creates patient-specific QA plans for single-isocentre SRS plans, measured with a Sun Nuclear SRS MapCHECK in a StereoPHAN. It recommends the minimum number of QA plans and the array position for each, creates and calculates the plans in Eclipse, and supports iterative fine-tuning of the array position.

## Status

| Phase | Content | State |
|---|---|---|
| 0 | Spike: test the risky ESAPI behaviour on Citrix | **Read-only part done**; write steps wait for script approval ([checklist](spike/SPIKE_CHECKLIST.md)) |
| 1 | Core: geometry, detector model, scoring, optimisation (+ unit tests) | **Done**: 84 tests passing |
| 2 | App shell on fake data | **Done**: full UI flow on demo data ([try it](docs/PHASE2_UI_CHECKLIST.md)) |
| 3 | ESAPI read path | not started |
| 4 | ESAPI write path | not started |
| 5 | Hardening, deployment docs | not started |

## Repository layout (so far)

```
Directory.Build.props              shared settings; EsapiPath (override in EsapiPath.user.props)
SrsMcPsqa.sln                      Core, Fake, App, tests (Esapi + Launcher come in Phase 3)
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
src/SrsMcPsqa.App/                 net48 x64 WPF: WPF-UI 3.0.5, CommunityToolkit.Mvvm 8.2.2, HelixToolkit.Wpf 2.25.0
  ViewModels/                      shell, patient & plan, PTVs, planning, review & fine-tune, settings
  Views/, MainWindow.xaml          Fluent UI; left navigation; progress panel
  Controls/PlaneView.cs            2D array-plane renderer (dose, PTVs, 100% isodose, array, diodes, rulers, drag)
  Controls/Scene3DView.cs          3D overview (HelixToolkit)
  Infrastructure/                  settings (JSON, department + per-user), rolling log, dialogs, converters
tests/SrsMcPsqa.Core.Tests/        net8.0 xUnit, synthetic PTVs with analytic dose; end-to-end workflow on the fake service
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

Core and tests (any OS with the .NET 8 SDK):

```bash
dotnet test SrsMcPsqa.sln
```

The app (Windows, .NET SDK 6+ or VS 2022; no Eclipse needed for demo data):

```bash
dotnet build SrsMcPsqa.sln -c Release
```

```bash
src/SrsMcPsqa.App/bin/Release/net48/SrsMcPsqa.exe --fake
```

The app project sets `EnableWindowsTargeting`, so the WPF code and XAML also compile on Linux CI (they can't run there).

The spike needs Windows, Visual Studio 2022 or the .NET SDK (6+), and the ESAPI v18 DLLs. See [spike/SPIKE_CHECKLIST.md](spike/SPIKE_CHECKLIST.md#a-build-windows-dev-machine-with-visual-studio-2022-or-the-net-sdk).

## VERIFY items

Status after the first Citrix run (ESAPI 18.1.0.472, API assembly 1.0.700.247). The probe confirmed every member below exists with the signature the code uses.

| # | Item | Result |
|---|---|---|
| V1 | Default `EsapiPath` for v18 | ESAPI DLLs are in the GAC on the Citrix server; set `EsapiPath.user.props` on the dev PC. |
| V2 | `CopyImageFromOtherPatient` | Returns `StructureSet`; there's also an overload taking a target `Study`, and `CanCopyImageFromOtherPatient(...)`. **Whether structures come across is still untested (needs approval).** Template `SRSMC_0_AXB` has one structure, BODY with assigned HU 250. |
| V3 | `BeginModifications` with patient open in Eclipse | **Pending:** blocked by script approval (`UnauthorizedScriptingAPIAccessException`). |
| V4 | `AddExternalPlanSetupAsVerificationPlan` | Exists. Behaviour pending approval. |
| V5 | `PlanSetup.Comment` writable | ✓ `{ get; set; }` |
| V6 | `ExternalBeamMachineParameters` | ✓ ctor (machine, energy, doseRate, technique, fluence) plus a settable `MLCId`. |
| V7 | `Add*Beam`, control-point editing | ✓ all present. `ControlPointParameters.MetersetWeight` and `GantryAngle` are settable. |
| V8 | Calculation model / options | `GetCalculationModel(PhotonVolumeDose)` returned **empty** for the test plan; the spike now also reports `PhotonCalculationModel`/`PhotonCalculationOptions`. |
| V9 | `CalculateDoseWithPresetValues` | ✓ exists. `CalculationResult` has **only `Success`**, with no messages, so the app reports beam calculation logs (`Beam.CalculationLogs`) instead. |
| V10 | `BeamParameters.Isocenter` setter | ✓ `{ get; set; }`, so no beam re-creation should be needed. |
| V11 | `Course.RemovePlanSetup` | ✓, plus `CanRemovePlanSetup`. |
| V12 | Dose units | Test plan is `DoseValuePresentation=Relative`, so `VoxelToDoseValue` returns %. Must switch to Absolute (the spike now tests whether that works read-only). `GetVoxels` takes ~190 ms per plane at 468×241, so extraction must read only the z-planes around the PTVs. |
| V13 | `TotalDose`, `RTPrescription` | ✓ `TotalDose` (30 Gy); `RTPrescription` null for the test plan. |
| V14 | Launcher `Assembly.Location` | **Empty** for a single-file plugin, so the config file next to the script can't work. Use the constant path or the environment variable. |
| V15 | ESAPI on a dedicated STA thread | ✓ `CreateApplication` in 1.7 s, calls fine. |
| V16 | WPF-UI on Citrix | Renders (to be confirmed visually). |

## Phase 1 design notes and decisions to review

These are choices I made where the brief left room. Each is easy to change.

1. **Prescription parser.** PTV names only ever carry the total dose (confirmed), so there is no per-fraction rule. Department convention (confirmed): `PTV30` = 30 Gy, so a two-digit number straight after "PTV" is read as Gy. A single digit (`PTV1`) is a PTV number. Parsed values outside 1–100 Gy are rejected. Structures starting with `z` are planning helpers: listed, but unticked by default (`HelperPrefixes` setting).
2. **Coverage and the diode rule.** If no position near a PTV's best plane puts N_min diodes inside it (a very small PTV), the diode rule is waived for that PTV and it is reported as **LowDiodeCount**. Confirmed acceptable: the plane still intersects the PTV, and PTVs below ~1 cm equivalent diameter aren't treated.
3. **"Partial coverage".** A PTV is partial when, on its primary plane, more than 1% of its cross-section lies outside the usable square (e.g. a PTV bigger than the array). The tolerance is configurable (`ClipTolerance`).
4. **Ties are centred.** Once covered PTVs are fully inside the usable square, many positions score the same. The optimiser then moves the array towards the centre of the covered PTVs, so they sit mid-array.
5. **Diode counts in the sweep.** These are expensive, so the diode rule is checked on the best candidate for each (covered set, orientation) rather than on every candidate. That candidate's set shrinks if the rule fails.
6. **Constrained re-optimisation.** Planes with assigned PTVs are optimised first, then the rest are covered by the free planes. Plans that are still unused get the plane adding the most to Σ max J_k, or else the best plane of the lowest-scoring PTV, so the requested plan count is honoured. If an assignment is impossible, the minimum J_k (not normalised) is maximised and a warning is given.
7. **Fine-tune constraint.** "Keep assigned PTVs inside the usable square" means no PTV may end up more clipped than it is now (or more than the clip tolerance, if that's larger), and PTVs on the plane must stay on it. If scores are equal, the smallest move is preferred, so an already-optimal plan gets Δ = 0.
8. **Manual edits.** The area and score terms use the nearest 0.5 mm depth (the cached tables). Diode counts use the exact plane.
9. **Limit.** At most 64 PTVs per model. Selected PTVs are passed in as the model's targets.

## Phase 2 notes

- **No JSON library.** Settings use `DataContractJsonSerializer` from .NET Framework, which avoids an assembly that could clash with what ESAPI loads.
- **Settings files.** Department defaults live in `SrsMcPsqa.settings.json` next to the exe (read-only, optional). Personal settings are in `%APPDATA%\SrsMcPsqa\settings.json`. "Reset to department defaults" deletes the personal file.
- **Errors.** Every error opens a dialog with **Copy details** and is logged to `%LOCALAPPDATA%\SrsMcPsqa\logs` (30 days kept).
- **Navigation.** It's a simple list rather than WPF-UI's `NavigationView`, which needs a page service. Screens unlock as the workflow progresses.
- **Review screen.** Each QA plan has its own tab. Values from before calculation and from fine-tune predictions are labelled as approximations. Calculated values are labelled "CALCULATED".
- **Array plane view.** u runs to the right, v (superior) up. At 0° patient left is on the right (seen from anterior). At 270° patient posterior is on the right.
