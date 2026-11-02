using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        public string LogFolder => Log.Folder;
        public string InstallationFolder => SettingsStore.InstallationFolder;
        public string TemplatePath => SettingsStore.TemplatePath;

        public override void OnNavigatedTo()
        {
            Edit = Session.Settings.Clone();
            Status = "";
        }

        [RelayCommand]
        private void Save()
        {
            try
            {
                if (Edit.PlanNamingPattern.Length == 0) Edit.PlanNamingPattern = Core.Models.QaPlanNaming.DefaultPattern;
                SettingsStore.SaveUser(Edit);
                var applied = Edit.Clone();
                Session.Settings = applied;
                Edit = applied.Clone();
                Log.Initialise(Edit.LogLevel);
                ApplyTheme(Edit.Theme);
                Status = $"Saved to {SettingsStore.UserPath}.";
                Log.Info("Settings saved.");
            }
            catch (System.Exception ex)
            {
                Session.Dialogs.ShowError("Save settings", ex.Message, ex);
            }
        }

        [RelayCommand]
        private void ResetToDepartment()
        {
            if (!Session.Dialogs.Confirm("Reset settings", "Discard your personal settings (including this installation's template phantom values) and use the department defaults?", "Reset")) return;
            try
            {
                SettingsStore.DeleteUser();
                Session.Settings = SettingsStore.LoadDepartmentDefaults();
                Edit = Session.Settings.Clone();
                ApplyTheme(Edit.Theme);
                Status = "Department defaults restored.";
            }
            catch (System.Exception ex)
            {
                Session.Dialogs.ShowError("Reset settings", ex.Message, ex);
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
