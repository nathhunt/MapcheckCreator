# Per-environment settings

Each environment (test Citrix, production Citrix) has its own copy of the app folder. In that folder, `SrsMcPsqa.settings.json` holds the department defaults for that environment, in particular the **template phantom patient**, which differs between test and production.

- `SrsMcPsqa.settings.TEST.json`: test environment (template patient `MC_DATASETS`, images `SRSMC_0_AXB` / `SRSMC_270_AXB`). **Fill in `TemplateStudyId`.**
- For production, copy it, set the production template patient, study and image IDs, and save it as `SrsMcPsqa.settings.json` in the production app folder.

When deploying, copy the right file over `SrsMcPsqa.settings.json` in the app folder. Template IDs are deliberately **not** taken from personal settings (`%APPDATA%` may follow you between environments). Use **Settings → Validate** in each environment to check them.

You can also enter the template IDs in the app (**Settings**). They are then saved for you for that installation folder only, so test and production values never mix. The department file is the default for everyone; the Settings values override it for you.
