# Deployment

Each environment (test Citrix, production Citrix) gets its own copy of the app folder, its own launcher build, and its
own script approval. Nothing is installed: the app runs from a folder the Citrix servers can read.

## 1. Build (Windows, Visual Studio 2022 or .NET SDK 6+)

1. On the build PC, point the build at the v18 ESAPI DLLs: create `EsapiPath.user.props` in the repository root
   (git-ignored) containing `<Project><PropertyGroup><EsapiPath>D:\esapi\18.0</EsapiPath></PropertyGroup></Project>`,
   with the folder holding `VMS.TPS.Common.Model.API.dll` and `VMS.TPS.Common.Model.Types.dll`.
   ESAPI is referenced with *Copy Local = false*: the DLLs come from Eclipse at run time (GAC on the Citrix server).
2. Build the app in Release:

   ```
   dotnet build src/SrsMcPsqa.App/SrsMcPsqa.App.csproj -c Release
   ```

   Output: `src/SrsMcPsqa.App/bin/Release/net48/` (x64). The version is shown in the window title.

## 2. Install the app folder

1. Copy the whole Release output folder to the environment's app location, e.g. a share readable from the Citrix
   servers. Keep one folder per environment (test, production).
2. In that folder, `SrsMcPsqa.settings.json` holds the **department defaults** for that environment. Start from
   `deploy/SrsMcPsqa.settings.TEST.json` for test; for production copy it and adjust. Site-specific values can also be
   entered in the app (**Settings**), see below.

## 3. Launcher (Eclipse plugin)

`src/SrsMcPsqa.Launcher/SrsMcPsqa.Launcher.cs` is a read-only single-file plugin that starts `SrsMcPsqa.exe` with
the current patient, course and plan. It finds the exe:

1. from the environment variable `SRSMCPSQA_EXE` (full path to the exe), if set on the Citrix servers, else
2. from the constant `DefaultExePath` at the top of the file.

A single-file plugin can't read a settings file next to itself (Eclipse gives it no file location), so the path is set
one of these two ways. Either set the environment variable (no rebuild when the folder moves), or edit
`DefaultExePath` for the environment. Copy the `.cs` file to the environment's Eclipse scripts folder.

## 4. Script approval

- **The app (`SrsMcPsqa.exe`)** writes to Eclipse (`[assembly: ESAPIScript(IsWriteable = true)]`), so it must be
  approved in Eclipse (Script Approvals, by an ARIA administrator with the right privileges) in each environment.
  **A rebuilt exe needs approving again.** Until approved, the app opens patients read-only and shows a yellow note;
  planning works, creating QA plans is disabled.
- **The launcher** is read-only and only starts the exe; approve it if your Eclipse requires approval for plugins.

## 5. Settings (Settings page in the app)

Per installation, entered once by a physicist (stored per user in `%APPDATA%\SrsMcPsqa\`; the department file is the
default for everyone):

| Setting | Notes |
|---|---|
| Template phantom: patient, 0° and 270° image IDs (study IDs optional) | Saved per installation folder, so test and production never mix. **Validate** checks them. |
| QA course ID | Default `QA`; created with the first QA plan if the patient doesn't have it. |
| Plan ID pattern | Default `MC{n}_{orient}`. |
| Collision check: MapCHECK origin height above the couch top, couch head end | **Required** for the collision check; see [docs/COLLISION_CHECK.md](../docs/COLLISION_CHECK.md). |
| Optimisation (edge margin, coverage threshold, minimum diodes, α) | Defaults are fine to start with. |

## 6. Files written at run time (per user)

| Path | Content |
|---|---|
| `%APPDATA%\SrsMcPsqa\settings.json`, `template-<hash>.json` | Personal settings; template phantom per installation |
| `%LOCALAPPDATA%\SrsMcPsqa\logs\srsmcpsqa-<date>.log` | Log (each step, timings, Eclipse calculation messages) |
| `%LOCALAPPDATA%\SrsMcPsqa\calc-timing.json` | Calculation timings for the progress estimate |

## 7. First run in a new environment

1. Launch from Eclipse on a test patient; check there's no yellow "Read-only" note (approval).
2. **Settings**: template phantom IDs → **Validate** (all green); collision check values; **Save**.
3. Create QA plans for a test plan and check them in Eclipse (see [docs/PHASE4_CHECKLIST.md](../docs/PHASE4_CHECKLIST.md)).
