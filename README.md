# SRS MapCHECK PSQA Plan Creator

**Version 1.0.3-beta** · Eclipse 18.1 (ESAPI) · Varian TrueBeam · Sun Nuclear SRS MapCHECK

Creates the patient-specific QA plans for a single-isocentre SRS plan in a few clicks. The app works out how few
MapCHECK measurements cover every PTV, places the array for each one, then creates and calculates the QA plans in
Eclipse with the clinical MUs. You can review the calculated dose on the array, fine-tune the position and recalculate.
It also warns you before a setup would bring the couch too close to the gantry.

## What it does for you

- **Fewest QA plans.** Suggests the smallest set of array planes (0° horizontal or 270° vertical) that measures every
  selected PTV, with the array placed where the diodes see the most of each PTV's high-dose region.
- **QA plans built for you.** Copies the phantom images, creates verification plans in the QA course (made for you if
  missing), copies the beams in clinical order, and calculates with one fraction at the clinical dose per fraction.
  Every beam's MU is checked against the clinical plan.
- **5×5 constancy field.** Adds a plan with a 5 × 5 cm, 100 MU field per energy at the array centre, gantry 0° or
  90° for each array orientation used (`MC_5x5_0`, `MC_5x5_270`), for the constancy check with each PSQA.
- **Clear positions.** The QA isocentre is always shown as the shift from the MapCHECK user origin, ready to set up at
  the linac. Shifts are rounded to the nearest 1 mm, and shifts under 2 mm to zero (both adjustable).
- **Collision warnings.** Checks the clearance between the TrueBeam head and the Exact IGRT couch for every beam, and
  suggests a lateral phantom offset when it's tight. It warns and never blocks.
- **Review and fine-tune.** Shows the calculated dose on the array with the PTVs and diodes, suggests a better
  position, previews the move, and recalculates the plans you choose.
- **Live progress.** Shows each plan's steps, Eclipse's own calculation progress, and the MU check.
- **Pick up later.** Finds the QA plans it made earlier for a plan, and flags any that no longer match the clinical plan.

## Before you start

| You need | Notes |
|---|---|
| Eclipse 18.1 with the app approved for writing | Your physics/ARIA administrator approves it in Script Approvals. Without approval the app opens read-only: you can plan, but not create QA plans. |
| A single-isocentre, head-first supine, photon SRS plan with calculated dose | Setup fields are fine; they're left out of the QA plans. |
| PTV names that carry the total dose, e.g. `PTV1_24Gy`, `PTV_2000cGy`, `PTV30` | Otherwise the plan prescription is used. You can change any Rx before planning. |
| The template phantom patient with the 0° and 270° SRS MapCHECK images | User origin at the centre of the array. Set once per installation in **Settings**. |

## Using the app

Open the clinical plan in Eclipse, then run **Tools → Scripts → SrsMcPsqa.Launcher**. The app opens on your patient and
plan.

1. **Patient & plan.** Check the plan and the QA course (default `PHYS_QA`), then **Load plan and continue**. To return
   to earlier QA plans, use **Find existing QA plans for this plan**.
2. **PTVs.** Tick the PTVs to measure and check each prescription. Helper structures (`z*`, `*eval*`) start unticked.
3. **Array planning.** Review the suggested QA plans. Select one to see where the array cuts the PTVs, move it if you
   want, and check the QA isocentre shift and couch clearance. Then **Create N QA plans**.
4. **Review & fine-tune.** Check each plan's calculated dose on the array. Optionally check for a better position,
   choose to use it, enter your own move or keep the current one, and recalculate.

At the linac, set up the MapCHECK at the QA isocentre shift shown on the plan's card. If the app suggested a lateral
phantom offset, move the phantom that far on the couch.

Step-by-step details are in the [user guide](docs/USER_GUIDE.md).

## Good to know

- **The clinical plan is never changed.** The app only creates, recalculates and deletes its own QA plans. It marks
  them with a tag in the plan Comment and will not touch any plan without it.
- **Calculation takes minutes.** Each QA plan is a full Eclipse calculation. **Cancel** stops after the current plan,
  so nothing is left half-made.
- **Approximations are labelled.** Positions before calculation, fine-tune previews and the collision check are
  approximations and say so. Calculated doses are labelled **CALCULATED**.
- **The collision check is a guide.** It models the head as a cylinder 37.5 cm from the isocentre (the laser guard
  trip radius) and the couch top with its real cross-section. Confirm unusual setups with a dry run. See
  [how it works](docs/COLLISION_CHECK.md).

## Settings

Everything on the **Settings** page except theme and log level is a **department setting**, shared by everyone who
uses the same installation. They're locked until you click the red **Edit department settings…** button and confirm.
Saving a change lists what changes and asks you to confirm again. Both need write access to the app folder. Department
settings are:

- the template phantom and the calculation model it's set up for (QA plans are only made for clinical plans calculated
  with that model)
- the QA course ID and plan ID pattern (`MC1_0`, `MC2_270`, …)
- the phantom position on the couch, collision tolerances and machine dimensions
- shift rounding (nearest step and threshold) and the optimisation values

## When something goes wrong

- **Error messages** explain what happened. **Copy details** puts the full error on the clipboard.
- **Logs** are in the `logs` folder next to the app, one file per user per computer:
  `srsmcpsqa_<user>_<computer>.log`. **Settings** shows the path of yours. The log records each step, Eclipse's
  messages, your choices and any error in full.
- **The version** in the window title includes the exact build, e.g. `1.0.3-beta+f1bc032`. Quote it with the log when
  reporting a problem.

## For administrators and developers

- **Installing and approving:** [deploy/README.md](deploy/README.md) covers building, the app folder, the launcher,
  script approval, folder permissions and the department settings file.
- **Building:** Windows with Visual Studio 2022 or the .NET SDK, and the ESAPI v18 DLLs (`EsapiPath.user.props`). In VS
  Code, **Ctrl+Shift+B** runs *Release build + launcher*. Try it without Eclipse with `SrsMcPsqa.exe --fake`, which
  uses demo data.
- **Tests:** `dotnet test tests/SrsMcPsqa.Core.Tests` runs on any OS. Without Eclipse, build against the stand-in DLLs
  in `tools/EsapiStubs`.
- **Background:** [technical notes](docs/TECHNICAL_NOTES.md) cover ESAPI behaviour, design decisions and algorithms.
  The `docs/PHASE*_CHECKLIST.md` files record the development and test history.

```
src/SrsMcPsqa.App/        the WPF app (screens, settings, logging); Esapi/ is everything that talks to Eclipse
src/SrsMcPsqa.Core/       planning, scoring, fine-tune and collision geometry (no Eclipse dependency)
src/SrsMcPsqa.Fake/       demo data for --fake
src/SrsMcPsqa.Launcher/   the Eclipse script that starts the app
tests/                    unit and workflow tests
deploy/                   department settings files and deployment guide
docs/                     user guide, collision check, technical notes, test checklists
```
