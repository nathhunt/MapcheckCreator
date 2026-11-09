# Phase 4: creating QA plans on Citrix (test system)

Phase 4 lets the app write to Eclipse. It copies the phantom images, creates and calculates the QA plans, moves isocentres for fine-tuning and deletes its own plans. **Use the test system and a test patient.**

## Before you start

- **Script approval:** the app, `SrsMcPsqa`, needs approval just like the spike did. If Eclipse shows the yellow "Read-only" note, it isn't approved yet. A rebuilt exe may need approving again.
- **Template phantom:** **Settings** must name the patient `MC_DATASETS` and the images `SRSMC_0_AXB` and `SRSMC_270_AXB`. Study IDs can stay empty. **Validate** should be all green.
- **Run time:** calculations take about 10 min per plan on the test system and will be much faster in production. The app stays responsive, but a calculation can't be interrupted.

## Build and install

As in Phase 3: `git pull`, build `src/SrsMcPsqa.App` in Release, copy the output folder to the test app location, and run it from the launcher.

## Run

| # | Do | Expect / check |
|---|---|---|
| 1 | Launch from Eclipse on `Phys_MapCheck_test` / `BRN HA multi`, with the patient open in Eclipse. | No yellow "Read-only" note. |
| 2 | Load the plan → PTVs → Array planning. Note the plans, e.g. 1 × 0° and 2 × 270°. | The footer button reads **Create N QA plans** and is enabled. |
| 3 | **Create N QA plans**, then confirm. | Progress panel: "Plan 1 of N" with an overall bar and elapsed time. A **Phantom images** card shows the 0° image **reused** and the 270° image **copied** (if its study ID is empty, the app briefly closes and re-opens the patient to look it up). Then one card per plan with its steps and their durations: create, prescription and model, copy beams, save, **Calculate dose**, Check MUs, read dose, save. |
| 3a | Watch the first **Calculate dose**. | It shows the model, beams and control points, and an elapsed timer with "no estimate yet" (the app hasn't timed this model on this computer yet). The bar moves without a percentage. |
| 3b | Watch the second plan's calculation. | Now it shows an estimated bar and "about X min left (est.)", and the overall line shows the time left in total. If it runs over 125 % of the estimate it says "taking longer than usual" in amber. |
| 3c | When all plans are done. | The panel stays open: every card has a tick and its total time, and the **MU check** table lists each beam's QA MU, clinical MU and difference with ticks. **Close** opens Review. |
| 4 | When done, the app opens **Review**. | One tab per plan with the calculated dose (green "CALCULATED"). |
| 5 | Check in Eclipse (re-open the patient, or refresh). | For each QA plan: it is in the QA course; IDs `SMC1_0`, `SMC2_270`, …; verification of `BRN HA multi`; **1 fraction × 5 Gy**; **beams in the same order as the clinical plan**; each beam's MU equals the clinical MU; same couch and collimator angles; isocentre at the planned position relative to the phantom user origin; calculation model AcurosXB; the Comment starts with `SRSMC-PSQA|v1`. |
| 6 | **Check for better positions**, then **Apply shifts and recalculate** for one plan. | The isocentre moves; the plan recalculates; the MUs still equal the clinical MUs; the tab updates. |
| 7 | Tick one plan's tab and **Delete ticked**. | The confirmation lists the plan; it is gone from Eclipse afterwards. Other plans in the course are untouched. |
| 8 | Close the app. | It closes cleanly. |

## Please send back

- Anything that differs from "Expect / check", with screenshots.
- The log `%LOCALAPPDATA%\SrsMcPsqa\logs\srsmcpsqa-<date>.log`, which records each step and its timing.
- Whether the time estimates were reasonable from the second plan on. Timings are kept in `%LOCALAPPDATA%\SrsMcPsqa\calc-timing.json` (seconds per control point per computer and model); delete it to start afresh.
- If a step fails: the error dialog text (**Copy details**). If a plan fails partway, the app removes the incomplete plan and says so.
