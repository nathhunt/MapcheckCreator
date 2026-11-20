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
                    if (_departmentTimeAtOpen != SettingsStore.DepartmentFileTimeUtc &&
                        !Session.Dialogs.Confirm("Department settings changed meanwhile",
                            "Someone changed the department settings file since you opened Settings. Saving now replaces their changes " +
                            "with yours. Click Cancel, then Reload, to see their version first.", "Save anyway", danger: true))
                        return;
                    string list = string.Join(System.Environment.NewLine, changes.Select(c => $"  {c.Field}: '{c.Old}' → '{c.New}'"));
                    if (!Session.Dialogs.Confirm("Change DEPARTMENT settings?",
                            "You are about to change the department settings. They apply to EVERYONE who uses this installation of the app" +
                            $" ({SettingsStore.InstallationFolder}), not just you.{System.Environment.NewLine}{System.Environment.NewLine}" +
                            $"Changes:{System.Environment.NewLine}{list}{System.Environment.NewLine}{System.Environment.NewLine}" +
                            $"File: {SettingsStore.DepartmentPath} (the previous version is kept as .bak).",
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
                    siteSaved = true;
                    Log.Info("Department settings changed: " + string.Join("; ", changes.Select(c => $"{c.Field} '{c.Old}' -> '{c.New}'")));
                }
                SettingsStore.SavePersonal(Edit);
                var applied = Edit.Clone();
                Session.Settings = applied;
                if (Session.Service is Esapi.EsapiPlanningService esapi) esapi.AutoSave = applied.AutoSave;
                Edit = applied.Clone();
                Log.Initialise(Edit.LogLevel);
                ApplyTheme(Edit.Theme);
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
                var loaded = SettingsStore.Load(out var source);
                Session.Settings = loaded;
                if (Session.Service is Esapi.EsapiPlanningService esapi) esapi.AutoSave = loaded.AutoSave;
                Edit = loaded.Clone();
                _departmentTimeAtOpen = SettingsStore.DepartmentFileTimeUtc;
                Status = "Reloaded: " + source + ".";
                Log.Info("Settings reloaded: " + source + ". " + Diagnostics.Settings(loaded));
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

        partial void OnEditChanged(AppSettings value) => ValidationResults.Clear();

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
