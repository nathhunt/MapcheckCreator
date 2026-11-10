# User guide

The app makes patient-specific QA plans for a single-isocentre SRS plan, measured with the SRS MapCHECK. It suggests
the fewest QA plans that measure every PTV, places the array for each, creates and calculates the plans in Eclipse with
the clinical MUs, and helps fine-tune the array position.

Start it from Eclipse with the clinical plan open: **Tools → Scripts → SrsMcPsqa.Launcher**.

## 1 · Patient & plan

- The patient, course and plan from Eclipse are preselected. Click **Load plan and continue**.
- **Course for the QA plans**: the QA course from Settings (default `QA`) is used, and created with the first QA plan if
  the patient doesn't have it. You can pick another open course.
- The checks list what the app found (dose, single isocentre, beams; setup fields are ignored). Errors stop here.
- **Find existing QA plans for this plan** loads QA plans the app made earlier for this plan into Review (only when you
  click it).

## 2 · PTVs

- Tick the PTVs to measure. Helper structures (`z*`, `*eval*` by default) are listed but unticked.
- Check each **Rx**: read from the PTV name (e.g. `PTV1_24Gy`, `PTV2_2000cGy`), else the plan prescription. Click a
  value to change it.

## 3 · Array planning

- The app suggests the QA plans (0° = array horizontal, 270° = vertical) and where the array cuts the PTVs. Select a
  plan to see its plane; the PTV table shows each PTV's QA plan, area on the array, diodes in the PTV and diodes ≥ Rx.
- The **QA isocentre** card shows the shift from the MapCHECK user origin (Left/Right, Ant/Post, Sup/Inf, mm and cm).
  It turns **amber/red** with a banner when the couch comes close to the gantry; the panel below gives the clearance per
  beam and, if one helps, a **suggested lateral phantom offset on the couch** for the measurement.
- Move the array with the arrows, by dragging it, or with **Exact position** (QA isocentre from the MapCHECK origin).
  **Reset to suggested** undoes your moves. **Change plans…** sets the number of plans, orientations and required PTVs.
- The dose bar on the right sets the colour-wash levels like Eclipse: drag the arrows or type values; **Reset** = 20 %
  to maximum.
- **Create N QA plans**: confirm (couch clearance warnings are listed but don't block). The progress panel shows each
  plan's steps, Eclipse's calculation progress, and the MU check. Cancel stops after the current plan.

## 4 · Review & fine-tune

- Each QA plan's calculated dose on the array (one fraction; Rx per fraction). Eclipse's calculation warnings are
  shown in amber.
- **Fine-tune** (optional), per plan: **1** Check this plan → **2** choose: the suggested move, your own move from the
  current position, or keep → the view shows a **preview** (approximate) and the shift and clearance after the move →
  **3** **Move isocentres and recalculate N plans** (footer). **Check all plans for better positions** does step 1 for
  every plan.
- Plans from an earlier session are marked; **OUT OF DATE** means their beams or MUs no longer match the clinical plan
  (delete and recreate them). **Recalculate this plan** appears for a plan without dose.
- **Delete plan** / **Delete all** remove the app's own QA plans from Eclipse (only tagged plans; phantom images stay).

## At the linac

- Set up the MapCHECK at the QA isocentre shown on the card (shift from the MapCHECK user origin).
- If the app suggested a lateral phantom offset, place the phantom that far towards the stated side on the couch.

## Settings

Template phantom (patient and image IDs, **Validate**), QA course ID, plan ID pattern, collision check geometry
(MapCHECK origin height above the couch is required), optimisation values, theme and log level. See
[deploy/README.md](../deploy/README.md).
