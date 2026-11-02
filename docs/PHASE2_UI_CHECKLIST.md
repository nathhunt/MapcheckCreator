# Phase 2: trying the app on demo data

The app runs on any Windows PC without Eclipse. It uses a synthetic patient (`FAKE001`, five metastases) and simulated Eclipse operations, and nothing is written anywhere except your own settings and log files.

## Build and start

```bash
dotnet build SrsMcPsqa.sln -c Release
```

```bash
src/SrsMcPsqa.App/bin/Release/net48/SrsMcPsqa.exe --fake
```

Without `--fake`, this build shows a note and starts on demo data anyway, because the Eclipse connection comes in Phase 3.

## Walk-through (please note anything odd)

| # | Do | Expect |
|---|---|---|
| 1 | Start the app. | An orange "DEMO DATA" bar. Patient FAKE001 opens by itself; course C1 and plan SRS_5Mets are preselected, and the QA course is "QA". C0_Old is completed, so it isn't offered as a QA course. |
| 2 | Click **Load plan**. | Validation messages appear, then the PTVs screen. |
| 3 | PTVs: check the Rx column. | 24, 20 (from cGy), 27, 18 come from the names. PTV5 has none in its name, so it gets the plan Rx of 20 Gy. Max cross-sections appear after a moment. Edit an Rx and its source changes to "user". `zPTV1_opt` is listed as a helper and is unticked. |
| 4 | **Next: array planning**. | The recommendation runs by itself: three plans, PTV1+PTV2 at 0°, PTV_R_Front+PTV4 at 270°, PTV5 alone. The plane view shows dose, PTV outlines (solid), 100% Rx isodose (dashed), the array, the usable square, diodes and mm rulers. |
| 5 | Click the other plans in the list; switch to **3D**. | The plane view follows the selection. 3D shows the PTVs, the arrays and L/R, A/P, S/I labels, with Anterior / Left / Superior / Fit view buttons. The default view is anterior, with patient left on screen right. |
| 6 | **Adjust array**: pick a step, use the arrow and depth buttons, **Centre on PTVs**, **Reset to suggested**, drag the array in the view. | The table and the plan's status update live, and the plan is marked "moved". Check that the button labels (R/L or A/P, S/I) match the view. |
| 7 | Open **Options**, change the orientation or the high dose ↔ PTV area slider. | **Recommend again** is highlighted; click it. Try "Only 270°". |
| 8 | **Change plans…**: set 2 plans, tick PTVs, set orientations, then **Find best positions**. | The result follows your choices. An impossible combination gives a warning in the yellow box. |
| 9 | **Create 3 QA plans** (footer), then confirm. | The progress panel shows phantom copy, plan creation and simulated calculation per plan (about 3 s each). **Cancel after current step** works between plans. |
| 10 | Review: look at each tab. | Calculated QA dose on the array plane (green "CALCULATED" label) and per-PTV metrics. The isocentre is shown relative to the phantom user origin. |
| 11 | **Check for better positions**. | Small proposed shifts. The demo "calculation" is offset by about 1 mm on purpose. Try Accept / Edit (type a move) / Discard, then **Apply shifts and recalculate**. Iterations count up and you can repeat. |
| 12 | Tick a tab and **Delete ticked**; then **Add new QA plans**. | Confirmation, then the plan disappears. "Add new" goes to the PTVs screen with PTVs not yet on a QA plan pre-ticked. |
| 13 | Settings: change the theme and Save; try **Validate** with empty fields. | Light/dark switches straight away. Validate lists what is missing. Settings are saved to `%APPDATA%\SrsMcPsqa\settings.json`. |
| 14 | Resize the window to 1366×768. | Everything is reachable; panels scroll where needed. |
| 15 | Check the log at `%LOCALAPPDATA%\SrsMcPsqa\logs`. | Operations are listed; the only patient identifier is the patient ID. |

Please send screenshots of anything that looks wrong, plus any error dialog text (use **Copy details**).
