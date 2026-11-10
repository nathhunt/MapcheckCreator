# UI feedback round 1 (2026-11-10): what changed and how to check it

Try it first with demo data (`SrsMcPsqa.exe --fake`), then on Citrix with a test patient.

| # | Feedback | Change | Check |
|---|---|---|---|
| 1 | Show the isocentre shift relative to the MapCHECK user origin, not the patient | Array planning: each plan card and the "Adjust array" panel show **Iso from MapCHECK origin** (e.g. "Left 35.0 mm, Post 24.5 mm, Sup 45.0 mm"). **Exact position** now edits the QA isocentre relative to the MapCHECK user origin (+L/−R, +P/−A, +S/−I). Review shows the same. | Values match the QA plan's isocentre relative to the phantom user origin in Eclipse. |
| 2 | Show which plan each PTV is on | Array planning PTV table: new **QA plan** column (bold when it is the plan you are looking at). | Change plans / move the array: the column follows. |
| 3 | Plan IDs `MC{n}_{angle}` | Default naming is now `MC1_0`, `MC2_270`, …; a saved old default (`SMC…`) is migrated. | Create a plan: its ID starts with `MC`. |
| 4 | Eclipse-style dose level control | Colour-wash bar next to both array views: drag the upper/lower arrows, or type values (Enter); **Reset** = lower 20 %, upper = maximum. 100 % = plan prescription (per fraction in Review). Shared by both pages. | Dragging updates the wash and the diode colours; dose below the lower level is not coloured. |
| 5 | Review & fine-tune unclear and cramped | Reworked like the planning page: plans on the left, dose in the middle, **Fine-tune** panel on the right with steps (1 check, 2 choose: suggested / own move / keep, 3 recalculate) and one main button in the footer. Deleting is per plan (**Delete plan**) or **Delete all**. | The next step is always obvious; the footer button reads "Move isocentres and recalculate N plans" only when something will move. |

Also fixed: Review compared the one-fraction QA dose with the whole-course Rx. It now uses Rx ÷ number of fractions (no change for single-fraction plans).
