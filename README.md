# SRS MapCHECK PSQA Plan Creator (Eclipse ESAPI v18)

Creates patient-specific QA plans for single-isocentre SRS plans, measured with a Sun Nuclear SRS MapCHECK in a StereoPHAN. It recommends the minimum number of QA plans and the array position for each, creates and calculates the plans in Eclipse, and supports iterative fine-tuning of the array position.

## Status

| Phase | Content | State |
|---|---|---|
| 0 | Spike: test the risky ESAPI behaviour on Citrix | **Code ready, waiting for Citrix test results** ([checklist](spike/SPIKE_CHECKLIST.md)) |
| 1 | Core: geometry, detector model, scoring, optimisation (+ unit tests) | not started |
| 2 | App shell on fake data | not started |
| 3 | ESAPI read path | not started |
| 4 | ESAPI write path | not started |
| 5 | Hardening, deployment docs | not started |

## Repository layout (so far)

```
Directory.Build.props              shared settings; EsapiPath (override in EsapiPath.user.props)
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

You need Windows, Visual Studio 2022 or the .NET SDK (6+), and the ESAPI v18 DLLs. See [spike/SPIKE_CHECKLIST.md](spike/SPIKE_CHECKLIST.md#a-build-windows-dev-machine-with-visual-studio-2022-or-the-net-sdk).

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
