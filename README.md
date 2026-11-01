# SRS MapCHECK PSQA Plan Creator (Eclipse ESAPI v18)

Creates patient-specific QA plans for single-isocentre SRS plans, measured with a Sun Nuclear SRS MapCHECK in a StereoPHAN. It recommends the minimum number of QA plans and the array position for each, creates and calculates the plans in Eclipse, and supports iterative fine-tuning of the array position.

## Status

| Phase | Content | State |
|---|---|---|
| 0 | Spike: test the risky ESAPI behaviour on Citrix | **Code ready, waiting for Citrix test results** ([checklist](spike/SPIKE_CHECKLIST.md)) |
| 1 | Core: geometry, detector model, scoring, optimisation (+ unit tests) | **Done**: 84 tests passing |
| 2 | App shell on fake data | not started |
| 3 | ESAPI read path | not started |
| 4 | ESAPI write path | not started |
| 5 | Hardening, deployment docs | not started |

## Repository layout (so far)

```
Directory.Build.props              shared settings; EsapiPath (override in EsapiPath.user.props)
SrsMcPsqa.sln                      Core + tests (app projects are added in later phases)
src/SrsMcPsqa.Core/                netstandard2.0, no ESAPI/WPF. Units mm and Gy, DICOM patient coordinates
  Geometry/                        Vec3, ArrayOrientation/PlaneAxes, IsoGeometry + ShiftLabels (sign conventions),
                                   DoseGrid, StructureMask, ContourRasterizer (even-odd)
  Detector/DiodeArray.cs           1,013-diode SRS MapCHECK lattice
  Prescription/                    PTV-ID prescription parser and resolver
  Optimisation/                    Scoring (J_k), PlanningModel (summed-area tables), CandidateSearch,
                                   SetCoverSolver, Recommender, ConstrainedOptimizer, FineTuneOptimizer
  Services/IPlanningService.cs     draft UI <-> Eclipse contract (revised after spike results)
tests/SrsMcPsqa.Core.Tests/        net8.0 xUnit, synthetic spherical PTVs with analytic dose
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

The spike needs Windows, Visual Studio 2022 or the .NET SDK (6+), and the ESAPI v18 DLLs. See [spike/SPIKE_CHECKLIST.md](spike/SPIKE_CHECKLIST.md#a-build-windows-dev-machine-with-visual-studio-2022-or-the-net-sdk).

## VERIFY items (open)

The spike either calls these through reflection (so it compiles whether or not they exist) or reports them in the step 0 API probe. Each will be marked resolved once there are Citrix results.

| # | Item | How the spike checks it |
|---|---|---|
| V1 | Default `EsapiPath` for v18 | build |
| V2 | `Patient.CopyImageFromOtherPatient` return type; whether structures/HU/couch come across | step 2 |
| V3 | `BeginModifications` while the patient is open in Eclipse | step 1 |
| V4 | `Course.AddExternalPlanSetupAsVerificationPlan` behaviour (beams copied? calc model inherited?) | step 3 |
| V5 | `PlanSetup.Comment` writable | step 3 (reflection) |
| V6 | `ExternalBeamMachineParameters` constructor; `MLCId`; `null` primary fluence mode for flattened beams | step 3 (MLCId via reflection) |
| V7 | `Add*Beam` signatures; `GetEditableParameters`/`ApplyParameters` leaf and jaw copy | step 3 |
| V8 | Calc model/options read/write; dose grid size via the options (`CalculationGridSizeInCM`?) | step 1b / step 3 |
| V9 | `CalculateDoseWithPresetValues` signature, result and messages | step 3 |
| V10 | `BeamParameters.Isocenter` setter (otherwise remove/recreate the beam) | step 3b (reflection) |
| V11 | `Course.RemovePlanSetup` | cleanup |
| V12 | `Dose.GetVoxels` / `VoxelToDoseValue` units (absolute vs relative presentation) | step 1b |
| V13 | `PlanSetup.TotalDose` (v16+ name), `RTPrescription` availability | step 1b |
| V14 | `Assembly.Location` for a single-file plugin (launcher config file next to the script) | launcher diagnostics |
| V15 | ESAPI on a non-UI STA thread works in a WPF standalone | steps 0/1 |
| V16 | WPF-UI 3.x on net48 under Citrix (theme, rendering) | step 4 (visual) |

## Phase 1 design notes and decisions to review

These are choices I made where the brief left room. Each is easy to change.

1. **Prescription parser.** I added an `N x D Gy` rule (e.g. `PTV_3x9Gy` → 27 Gy) before the plain `D Gy` rule, so per-fraction notation isn't read as the total. Parsed values outside 1–100 Gy are rejected.
2. **Coverage and the diode rule.** If no position near a PTV's best plane puts N_min diodes inside it (a very small PTV), the diode rule is waived for that PTV and it is reported as **LowDiodeCount**.
3. **"Partial coverage".** A PTV is partial when, on its primary plane, more than 1% of its cross-section lies outside the usable square (e.g. a PTV bigger than the array). The tolerance is configurable (`ClipTolerance`).
4. **Ties are centred.** Once covered PTVs are fully inside the usable square, many positions score the same. The optimiser then moves the array towards the centre of the covered PTVs, so they sit mid-array.
5. **Diode counts in the sweep.** These are expensive, so the diode rule is checked on the best candidate for each (covered set, orientation) rather than on every candidate. That candidate's set shrinks if the rule fails.
6. **Constrained re-optimisation.** Planes with assigned PTVs are optimised first, then the rest are covered by the free planes. Plans that are still unused get the plane adding the most to Σ max J_k, or else the best plane of the lowest-scoring PTV, so the requested plan count is honoured. If an assignment is impossible, the minimum J_k (not normalised) is maximised and a warning is given.
7. **Fine-tune constraint.** "Keep assigned PTVs inside the usable square" means no PTV may end up more clipped than it is now (or more than the clip tolerance, if that's larger), and PTVs on the plane must stay on it. If scores are equal, the smallest move is preferred, so an already-optimal plan gets Δ = 0.
8. **Manual edits.** The area and score terms use the nearest 0.5 mm depth (the cached tables). Diode counts use the exact plane.
9. **Limit.** At most 64 PTVs per model. Selected PTVs are passed in as the model's targets.
