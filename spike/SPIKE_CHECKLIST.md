# Phase 0 spike: Citrix test checklist

The spike is a small, write-enabled ESAPI v18 standalone app (`SrsMcPsqa.Spike.exe`) plus a read-only launcher plugin. It tests the risky parts of the design before the real app is built. **Use a test patient**, on a test or non-clinical Eclipse system if you have one. The spike copies images, creates plans and calculates dose.

Every action is logged with `[PASS]/[FAIL]/[WARN]/[INFO]` to the on-screen report and to
`%LOCALAPPDATA%\SrsMcPsqa\spike\spike-report-<timestamp>.txt`. Step 0 also writes `api-probe-<timestamp>.txt`. **Send both files back** with the answers in section D.

---

## A. Build (Windows dev machine with Visual Studio 2022 or the .NET SDK)

1. Copy `VMS.TPS.Common.Model.API.dll` and `VMS.TPS.Common.Model.Types.dll` (v18) to a folder, or use the Eclipse install folder.
   If your dev PC has no Eclipse installed, copy the two DLLs from an Eclipse workstation or Citrix server. To find them there, search the Varian install folder for `VMS.TPS.Common.Model.API.dll`.
2. If they aren't in `C:\Program Files (x86)\Varian\RTM\18.0\esapi\API`, create `EsapiPath.user.props` in the repo root (the folder holding `Directory.Build.props`). Without it, the build stops with "ESAPI DLLs not found in EsapiPath=...":
   ```xml
   <Project><PropertyGroup><EsapiPath>D:\esapi\18.0</EsapiPath></PropertyGroup></Project>
   ```
3. Build:
   ```bash
   dotnet build spike/SrsMcPsqa.Spike.sln -c Release
   ```
   Output: `spike/SrsMcPsqa.Spike/bin/Release/net48/`. **If the build fails, send me the full error list.** Neither the spike nor the launcher has been compiled against ESAPI yet (that isn't possible outside Eclipse), so compile errors tell me which ESAPI members differ in v18.
4. Copy the whole output folder to the Citrix share (e.g. `\\server\share\SrsMcPsqa\Spike\`). The ESAPI DLLs are deliberately not copied.

## B. Install on Citrix

1. **Script approval.** The exe is write-enabled (`[assembly: ESAPIScript(IsWriteable = true)]`). Register and approve it in Eclipse **Script Approvals** for the database you test on. Note which approval status was needed.
2. **Launcher.** Copy `SrsMcPsqa.Spike.Launcher/SrsMcPsqa.Spike.Launcher.cs` (and optionally `SrsMcPsqa.Spike.Launcher.txt`) into the Eclipse scripts folder. Point it at the exe by one of:
   - editing `DefaultExePath` in the `.cs` file (most reliable),
   - setting the user environment variable `SRSMCPSQA_SPIKE_EXE`, or
   - editing the `.txt` file next to the script (only works if Eclipse exposes the script's location; the launcher's diagnostics message shows this).
3. Test data needed:
   - a **template phantom patient** with the StereoPHAN + SRS MapCHECK image(s) (0° and/or 270°), with the user origin at the array centre and the body/density/couch structures as you'd use clinically
   - a **test patient** with a calculated single-isocentre SRS plan with ≥ 1 PTV
   - an empty **QA course** created in Eclipse in the test patient

## C. Run

Do the steps in order and keep the window open between steps (state is held in memory).

| # | Action | What it tests |
|---|---|---|
| C1 | Open the test patient and plan in Eclipse. Run the launcher from Tools > Scripts. | Launcher finds the exe, passes `--patient/--course/--plan`; the app starts on Citrix. |
| C2 | Look at the window. Click **Theme** twice. Check it fits a 1366×768 session. | **Step 4:** WPF UI renders (Fluent controls, icons, light/dark) on Citrix. |
| C3 | Click **0. API probe**. | ESAPI initialises on a background STA thread; lists v18 signatures for every VERIFY item. |
| C4 | Fill in the clinical course/plan IDs (prefilled from the launcher) and click **1. Open + modify** **while the patient is still open in Eclipse**. | **Step 1:** `BeginModifications` with the patient open in Eclipse. Also reads the plan (beams, calc model/options, dose grid, dose voxels, PTV contours). |
| C5 | If C4 failed: close the patient in Eclipse and click **1** again. If C4 passed: note whether Eclipse shows any lock/warning. | Whether "close in Eclipse, then Retry" is needed. |
| C6 | Enter template patient/study/image ID and the QA course. Click **2. Copy phantom**. | **Step 2:** `CopyImageFromOtherPatient` return value, which structures (body, density overrides, couch) and HU assignments come across, the user origin. |
| C7 | Click **2a. Inspect template**. | Opening the template read-only, reading its contours/HU, closing it and re-opening the target with modifications (the fallback for structure copying). |
| C8 | Click **3. Create QA plan + calc** (first beam only, offset e.g. `10, 0, 5`). | **Step 3:** verification plan link, plan ID/Comment writable, calc model/options/grid copy, beam copy (machine, energy, FFF, technique, MLC, leaves, weights), `CalculateDoseWithPresetValues` with clinical MUs. |
| C9 | In Eclipse (or after closing the app, see C12), check that the QA plan's beam matches the clinical beam: MLC sequence, angles, couch, MU and isocentre, plus the verification-plan link. | Beam copy correctness. |
| C10 | Click **3b. Move iso + recalc** (offset e.g. `12, 0, 3`). | Isocentre move via `BeamParameters.Isocenter`, or the remove/recreate fallback, then recalculation. |
| C11 | Optional: tick **Copy all treatment beams**, run **3** again, and note the calc time. | Full-plan copy and calc time on Citrix. |
| C12 | Close the app. Re-open the patient in Eclipse. | Saved changes appear after `SaveModifications`; the Application is disposed cleanly on exit. |
| C13 | Run the spike again, open the patient (1), click **Delete spike plans**. Note: only plans created in the *current* run are deleted, so this checks `RemovePlanSetup` on a plan you create in that run. | `RemovePlanSetup` + save. |

## D. Please report back

1. Build: did it compile? If not, the full error list.
2. Script approval: what was needed, and did it work?
3. Launcher: did it find the exe? Paste the diagnostics text from its message box (it shows what `Assembly.Location` returns).
4. C4/C5: did `BeginModifications` work **with the patient open in Eclipse**? Exact error text if not. Any lock or message in Eclipse?
5. C6: was a structure set returned? Which structures and HU overrides came across? Is the user origin right (array centre)?
6. C8: did the verification plan link appear? Plan ID and Comment set? Did the calculation succeed with **identical MUs**? Did the dose grid match the clinical plan?
7. C9: does the copied beam exactly match the clinical one? Any differences (MLC, jaws, collimator, couch, dose rate, FFF)?
8. C10: which isocentre-move path was used (`BeamParameters.Isocenter` or recreate)? Did the recalculation succeed?
9. Calc times for one beam and for all beams. Time to open the patient and read PTV contours (in the report).
10. UI: did the Fluent styling, icons and theme switching look right on Citrix? Any blurry text or rendering glitches?
11. Anything else unexpected: crashes, hangs on close, licence messages.
12. Clinical-practice answers I need before Phase 4:
    - Should the QA plan carry a **prescription**? (Step 3 can test it with the checkbox.)
    - Which **beam techniques** occur in your SRS plans (VMAT, DCA, static)?
    - Your QA **plan/beam naming** rules, if any, beyond `SMC{n}_{orient}`.
