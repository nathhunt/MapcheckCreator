# Phase 5 + UI round: test on Citrix

Build the latest `claude/adoring-ritchie-5cjxh0` in Release and install as in [deploy/README.md](../deploy/README.md).
The window title shows the version (0.9.0). A rebuilt exe may need approving again.

| # | Do | Expect / check |
|---|---|---|
| 1 | Settings: enter the MapCHECK origin height above the couch and the couch head end; Save. | A warning lists the changes and says they apply to everyone; after confirming, `SrsMcPsqa.settings.json` in the app folder is updated (`.bak` kept). Collision lines appear on the plan cards. |
| 1b | As a user **without** write access to the app folder, change a setting and Save. | "No write access"; nothing changes. |
| 1c | Another user opens the app. | Sees the same settings (template phantom, collision values, …); their own theme. |
| 2 | Test patient **without** a `PHYS_QA` course: load the plan. | QA course shows "PHYS_QA (new: created with the first QA plan)". |
| 3 | Create the QA plans. | The course `PHYS_QA` is created; plans `MC1_0`, … are in it. |
| 4 | Array planning / Review: [UI checklist](UI_FEEDBACK_1_CHECKLIST.md). | As listed there. |
| 5 | Move a plan's array ~10 cm sideways. | Shift card turns amber/red; a lateral phantom offset is suggested. |
| 6 | Close the app, start it again on the same plan, **Find existing QA plans for this plan**. | The plans load into Review, marked "From an earlier session", with dose, shift card and clearance. |
| 7 | (Optional) Re-optimise or change MUs of a copy of the clinical plan, make QA plans for it, change the clinical MUs again, Find existing. | The plan is flagged **OUT OF DATE**. |
| 8 | Clinical plan with a setup field. | The check says it's ignored; QA plans have no setup field. |

Please send back anything that differs, with screenshots, and the log (`%LOCALAPPDATA%\SrsMcPsqa\logs`).
