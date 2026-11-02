# Phase 3: reading real plans on Citrix (read-only)

Phase 3 connects the app to Eclipse for **reading** only: the patient, the clinical plan, its PTVs and the dose. The recommendation and the array planning then run on real data. Creating, moving and deleting QA plans comes in Phase 4; those buttons say so.

Reading needs **no script approval**. The app is still marked write-enabled for Phase 4, so it shows a yellow "Read-only" note until it is approved. That is expected.

## Build (dev PC, with `EsapiPath.user.props` as for the spike)

```bash
git pull
```

```bash
dotnet build src/SrsMcPsqa.App/SrsMcPsqa.App.csproj -c Release
```

Output: `src/SrsMcPsqa.App/bin/Release/net48/`.

## Install on the test Citrix environment

1. Copy the whole output folder to the test app location, e.g. `...\SrsMcPsqa\`.
2. Template phantom: either enter it in the app (**Settings**: patient `MC_DATASETS`, its study ID, `SRSMC_0_AXB`, `SRSMC_270_AXB`, then **Save**), or copy `deploy/SrsMcPsqa.settings.TEST.json` into the app folder as `SrsMcPsqa.settings.json` with `TemplateStudyId` filled in.
3. Launcher: copy `src/SrsMcPsqa.Launcher/SrsMcPsqa.Launcher.cs` into the Eclipse scripts folder. Set `DefaultExePath` to the exe from step 1.

## Run

| # | Do | Expect / note |
|---|---|---|
| 1 | Open a test patient with an SRS plan in Eclipse. Ideally use several small PTVs, which suits the array better than the large `BRN HA` PTVs. Run the launcher. | The app starts with the patient, course and plan from Eclipse, and the patient opens by itself (about 2 s for the first Eclipse connection). |
| 2 | Look at the yellow "Read-only" note. | Expected until approval. |
| 3 | **Load plan**. | Validation lines (dose, single isocentre, HFS, beams, total dose, calculation model), then the PTVs screen. Note how long loading takes: the dose is read slice by slice, about 0.2 s per slice. |
| 4 | PTVs screen. | `PTV30` = 30 Gy, `PTV25` and `PTV25_Eval` = 25 Gy (source "from name"). `z…` and `…eval…` structures are marked "helper" and unticked. Volumes match Eclipse. |
| 5 | **Continue → Recommend**. Look at the plane view and 3D. | Dose colourwash in Gy (the max shown at the bottom should match Eclipse's max dose in Gy), PTV outlines, dashed 100% isodose lines. Compare a plane with Eclipse: do the PTV outlines and the 100% line sit where Eclipse shows them? |
| 6 | Settings → **Validate**. | Opens the template patient read-only and checks the study, both images, the user origins and BODY, then re-opens your patient. |
| 7 | **Create QA plans**. | A clear message that this comes in Phase 4. Nothing is written. |
| 8 | Close the app. | It closes cleanly; the patient can be opened in Eclipse as usual. |

## Please send back

- Anything that differs from "Expect", with screenshots.
- The log file `%LOCALAPPDATA%\SrsMcPsqa\logs\srsmcpsqa-<date>.log`, which has timings for opening, PTV reading and dose reading.
- Whether the dose values (Gy) and PTV positions match Eclipse.
