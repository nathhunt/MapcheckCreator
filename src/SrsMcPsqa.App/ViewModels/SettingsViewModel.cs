using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SrsMcPsqa.App.Infrastructure;
using SrsMcPsqa.Core.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SrsMcPsqa.App.ViewModels
{
    /// <summary>Screen 6: settings (template phantom, naming, optimisation defaults, theme, logging).</summary>
    public sealed partial class SettingsViewModel : PageViewModel
    {
        public SettingsViewModel(ShellViewModel shell) : base(shell, "Settings", SymbolRegular.Settings24)
        {
            _edit = shell.Session.Settings.Clone();
        }

        public static IReadOnlyList<string> Themes { get; } = new[] { "System", "Light", "Dark" };
        public static IReadOnlyList<string> LogLevels { get; } = new[] { "Debug", "Info", "Warn", "Error" };

        /// <summary>Working copy; saved with Save.</summary>
        [ObservableProperty] private AppSettings _edit;
        [ObservableProperty] private string _status = "";

        public ObservableCollection<ValidationMessage> ValidationResults { get; } = new ObservableCollection<ValidationMessage>();

        public string UserPath => SettingsStore.UserPath;
        public string DepartmentPath => SettingsStore.DepartmentPath;
        public string LogFolder => Log.CurrentFile;
        public string InstallationFolder => SettingsStore.InstallationFolder;
        /// <summary>Site settings file time when this screen was opened (to detect someone else's change).</summary>
        private System.DateTime? _departmentTimeAtOpen;

        public override void OnNavigatedTo()
        {
            Edit = Session.Settings.Clone();
            _departmentTimeAtOpen = SettingsStore.DepartmentFileTimeUtc;
            Status = "";
            IsUnlocked = false;
            RefreshModels();
        }

        // ------------------------------------------------------------------ department settings lock
        /// <summary>
        /// Department settings are read-only until unlocked (red button + confirmation); they lock again after Save, Reload,
        /// Lock or leaving the page. A guard against accidental edits; the folder permissions are the real protection.
        /// </summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsLocked))] private bool _isUnlocked;
        public bool IsLocked => !IsUnlocked;

        [RelayCommand]
        private void Unlock()
        {
            if (!SettingsStore.CanWriteInstallationFolder(out var why))
            {
                Session.Dialogs.ShowInfo("No write access",
                    $"You can't change the department settings: {why}{System.Environment.NewLine}{System.Environment.NewLine}" +
                    $"Folder: {SettingsStore.InstallationFolder}{System.Environment.NewLine}Ask someone with write access to the app folder.");
                Log.Info("Department settings unlock refused: no write access (" + why + ").");
                return;
            }
            if (!Session.Dialogs.Confirm("Edit DEPARTMENT settings?",
                    "⚠ You are about to unlock the department settings." + System.Environment.NewLine + System.Environment.NewLine +
                    $"They apply to EVERYONE who uses this installation ({SettingsStore.InstallationFolder}), and they control how QA plans " +
                    "are created and checked: the template phantom and its calculation model, the QA course and plan names, the collision " +
                    "check, shift rounding and the optimisation." + System.Environment.NewLine + System.Environment.NewLine +
                    "A wrong value can give wrong QA plans for every user. Only continue if you are authorised to change them and know " +
                    "what each change does." + System.Environment.NewLine + System.Environment.NewLine +
                    "Nothing changes until you Save, which lists every change and asks again.",
                    "Unlock for editing", danger: true))
            {
                Log.Info("Department settings unlock cancelled.");
                return;
            }
            IsUnlocked = true;
            Status = "Department settings unlocked for editing.";
            Log.Info("Department settings unlocked for editing.");
        }

        [RelayCommand]
        private void Lock()
        {
            var changes = SettingsStore.SiteDifferences(Session.Settings, Edit);
            if (changes.Count > 0 && !Session.Dialogs.Confirm("Discard changes?",
                    $"Lock the department settings and discard {changes.Count} unsaved change(s)?", "Discard and lock"))
                return;
            string theme = Edit.Theme, logLevel = Edit.LogLevel;
            Edit = Session.Settings.Clone();
            Edit.Theme = theme; Edit.LogLevel = logLevel;
            IsUnlocked = false;
            RefreshModels();
            Status = changes.Count > 0 ? "Locked; unsaved department changes discarded." : "Locked.";
            Log.Info("Department settings locked" + (changes.Count > 0 ? $" ({changes.Count} unsaved change(s) discarded)." : "."));
        }

        // ------------------------------------------------------------------ template phantom calculation model
        /// <summary>Photon dose models Eclipse offers for the loaded clinical plan, plus the saved one if it isn't among them.</summary>
        public ObservableCollection<string> CalculationModels { get; } = new ObservableCollection<string>();
        [ObservableProperty] private string _calculationModelHint = "";

        partial void OnEditChanged(AppSettings value) { ValidationResults.Clear(); RefreshModels(); }

        private void RefreshModels()
        {
            if (Edit == null) return;
            var available = Session.ClinicalPlan?.AvailableCalculationModels ?? new List<string>();
            CalculationModels.Clear();
            foreach (var m in available) CalculationModels.Add(m);
            string saved = Edit.TemplateCalculationModel?.Trim() ?? "";
            bool listed = available.Any(m => string.Equals(m, saved, System.StringComparison.OrdinalIgnoreCase));
            if (saved.Length > 0 && !listed) CalculationModels.Insert(0, saved);
            CalculationModelHint =
                saved.Length == 0 ? "Not set: creating QA plans is off until it is." :
                Session.ClinicalPlan == null ? "Load a clinical plan (step 1) to list the models Eclipse offers." :
                !listed ? $"'{saved}' isn't among the models Eclipse offers for the loaded plan." :
                Session.ClinicalPlan.CalculationModel.Length > 0
                    ? $"The loaded plan is calculated with {Session.ClinicalPlan.CalculationModel}." : "";
        }

        /// <summary>
        /// Saves personal preferences (theme, log level) for this user, and, after a warning and confirmation, changed site
        /// settings to the department file in the app folder, which applies to everyone using this installation.
        /// </summary>
        [RelayCommand]
        private void Save()
        {
            try
            {
                if (Edit.PlanNamingPattern.Length == 0) Edit.PlanNamingPattern = Core.Models.QaPlanNaming.DefaultPattern;
                var changes = SettingsStore.SiteDifferences(Session.Settings, Edit);
                bool siteSaved = false;
                if (changes.Count > 0)
                {
                    // Checked before anything is written: these settings apply to everyone using this installation.
                    var problems = Edit.Problems();
                    if (problems.Count > 0)
                    {
                        Session.Dialogs.ShowInfo("Check the settings",
                            "These department settings can't be used, so nothing was saved:" + System.Environment.NewLine + System.Environment.NewLine +
                            string.Join(System.Environment.NewLine, problems.Select(x => "  • " + x)));
                        return;
                    }
                    if (_departmentTimeAtOpen != SettingsStore.DepartmentFileTimeUtc &&
                        !Session.Dialogs.Confirm("Department settings changed meanwhile",
                            "Someone changed the department settings file since you opened Settings. Saving now replaces their changes " +
                            "with yours. Click Cancel, then Reload, to see their version first.", "Save anyway", danger: true))
                        return;
                    string list = string.Join(System.Environment.NewLine, changes.Select(c => $"  {c.Field}: '{c.Old}' → '{c.New}'"));
                    // The file couldn't be read: the "old" values are built-in defaults, and saving replaces the file.
                    string unreadable = Session.SettingsError == null ? "" :
                        $"{System.Environment.NewLine}{System.Environment.NewLine}The current file could not be read ({Session.SettingsError}). " +
                        "Saving REPLACES it with the built-in defaults plus the changes above; the unreadable file is kept as .bak. " +
                        "If someone is fixing it by hand, click Cancel.";
                    if (!Session.Dialogs.Confirm("Change DEPARTMENT settings?",
                            "You are about to change the department settings. They apply to EVERYONE who uses this installation of the app" +
                            $" ({SettingsStore.InstallationFolder}), not just you.{System.Environment.NewLine}{System.Environment.NewLine}" +
                            $"Changes:{System.Environment.NewLine}{list}{System.Environment.NewLine}{System.Environment.NewLine}" +
                            $"File: {SettingsStore.DepartmentPath} (the previous version is kept as .bak).{unreadable}",
                            "Change department settings", danger: true))
                        return;
                    try { SettingsStore.SaveDepartment(Edit); }
                    catch (System.UnauthorizedAccessException)
                    {
                        Session.Dialogs.ShowInfo("No write access",
                            $"You don't have permission to change the department settings file:{System.Environment.NewLine}{SettingsStore.DepartmentPath}" +
                            $"{System.Environment.NewLine}{System.Environment.NewLine}Nothing was changed. Ask someone with write access to the app folder.");
                        return;
                    }
                    _departmentTimeAtOpen = SettingsStore.DepartmentFileTimeUtc;
                    Session.SettingsError = null; // the file now holds what was just written
                    siteSaved = true;
                    Log.Info("Department settings changed: " + string.Join("; ", changes.Select(c => $"{c.Field} '{c.Old}' -> '{c.New}'")));
                }
                SettingsStore.SavePersonal(Edit);
                var applied = Edit.Clone();
                Session.Settings = applied;
                if (Session.Service is Esapi.EsapiPlanningService esapi) { esapi.AutoSave = applied.AutoSave; esapi.RequiredCalculationModel = applied.TemplateCalculationModel; }
                Edit = applied.Clone();
                Log.Initialise(Edit.LogLevel);
                ApplyTheme(Edit.Theme);
                IsUnlocked = false;
                Status = siteSaved ? $"Department settings saved ({changes.Count} change(s)); personal preferences saved." : "Personal preferences saved.";
            }
            catch (System.Exception ex)
            {
                Session.Dialogs.ShowError("Save settings", ex.Message, ex);
            }
        }

        /// <summary>Discards unsaved edits and re-reads the department settings file (e.g. after someone else changed it).</summary>
        [RelayCommand]
        private void Reload()
        {
            try
            {
                var result = SettingsStore.Load();
                var loaded = result.Settings;
                foreach (var w in result.Warnings) Log.Warn(w);
                Session.Settings = loaded;
                Session.SettingsError = result.DepartmentError;
                if (Session.Service is Esapi.EsapiPlanningService esapi) { esapi.AutoSave = loaded.AutoSave; esapi.RequiredCalculationModel = loaded.TemplateCalculationModel; }
                Edit = loaded.Clone();
                _departmentTimeAtOpen = SettingsStore.DepartmentFileTimeUtc;
                IsUnlocked = false;
                Status = "Reloaded: " + result.Source + ".";
                Log.Info("Settings reloaded: " + result.Source + ". " + Diagnostics.Settings(loaded));
            }
            catch (System.Exception ex)
            {
                Session.Dialogs.ShowError("Reload settings", ex.Message, ex);
            }
        }

        [RelayCommand]
        private async Task ValidateAsync()
        {
            ValidationResults.Clear();
            await Shell.RunAsync("Validating template phantom", async (p, ct) =>
            {
                p.BeginStep($"Opening template patient '{Edit.TemplatePatientId}' read-only...");
                var results = await Session.Service.ValidateTemplateAsync(Edit.ToTemplate(), ct);
                foreach (var r in results) ValidationResults.Add(r);
            });
        }


        public static void ApplyTheme(string theme)
        {
            switch (theme)
            {
                case "Light": ApplicationThemeManager.Apply(ApplicationTheme.Light); break;
                case "Dark": ApplicationThemeManager.Apply(ApplicationTheme.Dark); break;
                default: ApplicationThemeManager.ApplySystemTheme(); break;
            }
        }
    }
}
