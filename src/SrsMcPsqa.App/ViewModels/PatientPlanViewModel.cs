using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SrsMcPsqa.App.Infrastructure;
using SrsMcPsqa.Core.Services;
using Wpf.Ui.Controls;

namespace SrsMcPsqa.App.ViewModels
{
    /// <summary>Screen 1: open the patient, choose the clinical plan and the QA course.</summary>
    public sealed partial class PatientPlanViewModel : PageViewModel
    {
        public PatientPlanViewModel(ShellViewModel shell) : base(shell, "Patient & plan", SymbolRegular.Person24, 1)
        {
            _patientId = shell.Session.Launch.PatientId;
            if (string.IsNullOrEmpty(_patientId) && shell.IsFake) _patientId = "FAKE001";
        }

        [ObservableProperty] private string _patientId;
        [ObservableProperty] private string _patientText = "No patient open.";
        [ObservableProperty] private string? _modifyError;
        [ObservableProperty] private CourseSummary? _selectedCourse;
        [ObservableProperty] private PlanSummary? _selectedPlan;
        [ObservableProperty] private CourseSummary? _selectedQaCourse;

        public ObservableCollection<CourseSummary> Courses { get; } = new ObservableCollection<CourseSummary>();
        public ObservableCollection<PlanSummary> Plans { get; } = new ObservableCollection<PlanSummary>();
        public ObservableCollection<CourseSummary> QaCourses { get; } = new ObservableCollection<CourseSummary>();
        public ObservableCollection<ValidationMessage> Validation { get; } = new ObservableCollection<ValidationMessage>();

        public bool HasNoQaCourse => Session.Patient != null && QaCourses.Count == 0;

        public string PhantomStatus => Session.Settings.HasTemplate
            ? $"Template phantom: patient '{Session.Settings.TemplatePatientId}', images '{Session.Settings.Template0ImageId}' (0°) and '{Session.Settings.Template270ImageId}' (270°)."
            : "Template phantom images are not configured for this installation. Set them in Settings before creating QA plans.";

        public bool PhantomConfigured => Session.Settings.HasTemplate;

        private bool _autoOpened;

        public override void OnNavigatedTo()
        {
            OnPropertyChanged(nameof(PhantomStatus));
            OnPropertyChanged(nameof(PhantomConfigured));
            if (!_autoOpened && !string.IsNullOrWhiteSpace(PatientId))
            {
                _autoOpened = true;
                _ = OpenPatientAsync();
            }
        }

        [RelayCommand]
        private async Task OpenPatientAsync()
        {
            if (string.IsNullOrWhiteSpace(PatientId)) return;
            ModifyError = null;
            await Shell.RunAsync($"Opening patient {PatientId.Trim()}", async (p, ct) =>
            {
                p.BeginStep("Opening patient...");
                var patient = await Session.Service.OpenPatientAsync(PatientId.Trim(), ct);
                Session.Patient = patient;
                PatientText = $"{patient.Id} · {patient.DisplayName}";
                if (!patient.CanModify)
                {
                    ModifyError = "Eclipse doesn't allow this app to change the patient: the app isn't approved yet, or the patient is " +
                                  "locked (close it in Eclipse, then Retry). You can still plan; creating QA plans stays disabled.";
                    Infrastructure.Log.Info("Read-only: " + patient.ModifyError);
                }
                FillCourses(patient);
            });
        }

        private void FillCourses(PatientSummary patient)
        {
            Courses.Clear(); QaCourses.Clear(); Plans.Clear();
            foreach (var c in patient.Courses) Courses.Add(c);
            foreach (var c in patient.Courses.Where(c => !c.IsCompleted)) QaCourses.Add(c);
            var launch = Session.Launch;
            SelectedCourse = Courses.FirstOrDefault(c => c.Id == launch.CourseId)
                             ?? Courses.FirstOrDefault(c => c.Plans.Any(p => !p.IsQaPlan));
            if (SelectedPlan == null) SelectedPlan = Plans.FirstOrDefault(p => p.PlanId == launch.PlanId) ?? Plans.FirstOrDefault();
            // The configured QA course if it exists; otherwise offer to create it (it's made with the first QA plan).
            string qaId = string.IsNullOrWhiteSpace(Session.Settings.QaCourseId) ? "PHYS_QA" : Session.Settings.QaCourseId.Trim();
            var configured = QaCourses.FirstOrDefault(c => string.Equals(c.Id, qaId, System.StringComparison.OrdinalIgnoreCase));
            if (configured == null && !patient.Courses.Any(c => string.Equals(c.Id, qaId, System.StringComparison.OrdinalIgnoreCase)))
            {
                configured = new CourseSummary { Id = qaId, WillBeCreated = true };
                QaCourses.Insert(0, configured);
            }
            SelectedQaCourse = configured
                               ?? QaCourses.FirstOrDefault(c => c.Id.IndexOf("QA", System.StringComparison.OrdinalIgnoreCase) >= 0 && c != SelectedCourse)
                               ?? QaCourses.FirstOrDefault(c => c != SelectedCourse);
            OnPropertyChanged(nameof(HasNoQaCourse));
        }

        partial void OnSelectedCourseChanged(CourseSummary? value)
        {
            Plans.Clear();
            if (value == null) return;
            foreach (var p in value.Plans.Where(p => !p.IsQaPlan)) Plans.Add(p);
            SelectedPlan = Plans.FirstOrDefault(p => p.PlanId == Session.Launch.PlanId) ?? Plans.FirstOrDefault();
        }

        partial void OnSelectedQaCourseChanged(CourseSummary? value) => Session.QaCourse = value;

        [RelayCommand]
        private async Task LoadPlanAsync()
        {
            if (SelectedCourse == null || SelectedPlan == null) return;
            Validation.Clear();
            bool ok = await Shell.RunAsync($"Loading plan {SelectedPlan.PlanId}", async (p, ct) =>
            {
                p.BeginStep("Reading plan, PTVs and dose...");
                var data = await Session.Service.LoadClinicalPlanAsync(SelectedCourse.Id, SelectedPlan.PlanId, p.Reporter, ct);
                foreach (var v in data.Validation) Validation.Add(v);
                if (data.Validation.Any(v => v.Severity == ValidationSeverity.Error))
                    throw new System.InvalidOperationException("The plan can't be used for PSQA: " +
                        string.Join(" ", data.Validation.Where(v => v.Severity == ValidationSeverity.Error).Select(v => v.Text)));
                Session.SetClinicalPlan(data);
                if (Session.CalculationModelBlockedReason is string why)
                    Validation.Add(new ValidationMessage(ValidationSeverity.Warning, "QA plans can't be created for this plan. " + why));
                else if (CalculationModelCheck.IsUnknown(data.CalculationModel) && !string.IsNullOrWhiteSpace(Session.Settings.TemplateCalculationModel))
                    Validation.Add(new ValidationMessage(ValidationSeverity.Info,
                        $"Eclipse doesn't report this plan's calculation model, so it's checked on the first QA plan, which gets the clinical " +
                        $"model. If that isn't {Session.Settings.TemplateCalculationModel}, the plan is removed straight away and nothing else is created."));
            });
            if (!ok) return;
            if (Session.CalculationModelBlockedReason is string reason)
            {
                Log.Warn("Calculation model check: " + reason);
                Session.Dialogs.ShowInfo("QA plans can't be created for this plan",
                    reason + Environment.NewLine + Environment.NewLine + "You can still look at the array planning, but Create is off.");
            }
            Shell.Navigate(Shell.Ptvs);
        }

        /// <summary>Loads QA plans made earlier for the loaded plan (only on request), then shows them in Review.</summary>
        [RelayCommand]
        private async Task FindExistingQaPlansAsync()
        {
            if (!Session.HasPlan) { Session.Dialogs.ShowInfo("Find existing QA plans", "Load the clinical plan first."); return; }
            if (await Shell.Review.FindExistingAsync() > 0 || Session.QaPlans.Count > 0) Shell.Navigate(Shell.Review);
        }

        [RelayCommand]
        private void OpenSettings() => Shell.Navigate(Shell.Settings);
    }
}
