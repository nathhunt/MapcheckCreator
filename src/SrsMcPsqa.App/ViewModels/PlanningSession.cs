using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SrsMcPsqa.App.Infrastructure;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Optimisation;
using SrsMcPsqa.Core.Prescription;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.App.ViewModels
{
    /// <summary>One PTV row: include flag, prescription (editable) and display data.</summary>
    public sealed partial class PtvRowViewModel : ObservableObject
    {
        private bool _settingRx;

        public PtvRowViewModel(int index, PtvInfo info, string helperPatterns)
        {
            Index = index; Info = info;
            Colour = PtvColours.For(index);
            Brush = new SolidColorBrush(Colour);
            Brush.Freeze();
            _settingRx = true;
            RxGy = info.RxGy;
            _settingRx = false;
            RxSource = info.RxSource;
            IsHelper = PtvFilter.IsHelper(info.Id, helperPatterns);
            Include = info.RxGy > 0 && !IsHelper;
        }

        public int Index { get; }
        public PtvInfo Info { get; }

        /// <summary>Planning helper (e.g. z-prefixed or eval): listed but unticked by default.</summary>
        public bool IsHelper { get; }
        public string Note => IsHelper ? "helper" : "";
        public string Id => Info.Id;
        public double VolumeCc => Info.VolumeCc;
        public Color Colour { get; }
        public SolidColorBrush Brush { get; }

        [ObservableProperty] private bool _include;
        [ObservableProperty] private double _rxGy;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(RxSourceText))] private RxSource _rxSource;
        [ObservableProperty] private double? _maxArea0;
        [ObservableProperty] private double? _maxArea270;

        public string RxSourceText => RxSource.Describe();

        partial void OnRxGyChanged(double value)
        {
            if (!_settingRx) RxSource = RxSource.User;
        }

        public PlanningTarget ToTarget() => new PlanningTarget(Id, RxGy, Info.Mask!);

        /// <summary>Target with Rx scaled, e.g. ÷ fractions to compare with the dose of a one-fraction QA plan.</summary>
        public PlanningTarget ToTarget(double rxScale) => new PlanningTarget(Id, RxGy * rxScale, Info.Mask!);
    }

    /// <summary>
    /// Everything shared between screens for the open patient. Owned by the shell; screens read and update it.
    /// All members are used on the UI thread.
    /// </summary>
    /// <summary>Dose colour-wash levels shared by the array-plane views (% of the reference dose), like Eclipse's colour wash bar.</summary>
    public sealed partial class DoseWashViewModel : ObservableObject
    {
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(LowerText))] private double _lowerPercent = Rendering.DoseWash.DefaultLowerPercent;
        /// <summary>≤ 0 = maximum dose on the plane.</summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(UpperText))] private double _upperPercent;

        public string LowerText
        {
            get => LowerPercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            set
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0)
                    LowerPercent = UpperPercent > 0 ? Math.Min(v, UpperPercent - 1) : v;
                OnPropertyChanged();
            }
        }

        /// <summary>Upper level as text; empty or "max" = maximum dose on the plane.</summary>
        public string UpperText
        {
            get => UpperPercent > 0 ? UpperPercent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "max";
            set
            {
                var t = (value ?? "").Trim();
                if (t.Length == 0 || t.Equals("max", StringComparison.OrdinalIgnoreCase)) UpperPercent = 0;
                else if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > LowerPercent)
                    UpperPercent = v;
                OnPropertyChanged();
            }
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void Reset() { LowerPercent = Rendering.DoseWash.DefaultLowerPercent; UpperPercent = 0; }
    }

    public sealed partial class PlanningSession : ObservableObject
    {
        /// <summary>Colour-wash levels, shared by the planning and review views.</summary>
        public DoseWashViewModel DoseWash { get; } = new DoseWashViewModel();

        public PlanningSession(IPlanningService service, AppSettings settings, LaunchOptions launch, IDialogService dialogs, bool isDemo)
        {
            Service = service; Settings = settings; Launch = launch; Dialogs = dialogs; IsDemo = isDemo;
        }

        /// <summary>Synthetic demo data (no template phantom needed).</summary>
        public bool IsDemo { get; }

        public IPlanningService Service { get; }
        public LaunchOptions Launch { get; }
        public IDialogService Dialogs { get; }

        [ObservableProperty] private AppSettings _settings = null!;
        [ObservableProperty] private PatientSummary? _patient;
        [ObservableProperty] private ClinicalPlanData? _clinicalPlan;
        [ObservableProperty] private CourseSummary? _qaCourse;

        public ObservableCollection<PtvRowViewModel> Ptvs { get; } = new ObservableCollection<PtvRowViewModel>();
        public ObservableCollection<QaPlanViewModel> QaPlans { get; } = new ObservableCollection<QaPlanViewModel>();
        public Dictionary<ArrayOrientation, PhantomImageInfo> Phantoms { get; } = new Dictionary<ArrayOrientation, PhantomImageInfo>();

        /// <summary>Raised after a clinical plan (and its PTV rows) is loaded.</summary>
        public event EventHandler? PlanLoaded;

        public bool HasPlan => ClinicalPlan != null;

        /// <summary>100 % of the colour wash: the plan's total dose, else the highest PTV Rx (whole course).</summary>
        public double? ReferenceGy => ClinicalPlan?.PlanTotalDoseGy is double t && t > 0 ? t
            : Ptvs.Count > 0 ? Ptvs.Max(r => r.RxGy) : (double?)null;

        /// <summary>Why QA plans can't be created right now, in plain words; null when they can.</summary>
        public string? CreateBlockedReason
        {
            get
            {
                if (!Service.SupportsWriting) return "This version reads plans only. Creating QA plans in Eclipse comes in the next version.";
                if (SettingsError != null) return "Creating is off: the department settings file could not be read, so built-in defaults are in use. Fix the file, then Settings → Reload.";
                if (Patient == null) return "Open a patient first (step 1).";
                if (!Patient.CanModify) return "Eclipse doesn't allow changes to this patient yet (the app isn't approved, or the patient is locked). You can still plan.";
                if (QaCourse == null) return "Choose a QA course in step 1.";
                if (NeedsTemplateSettings) return "Creating is off: the template phantom (patient and image IDs) isn't set for this installation. Open Settings, enter it and Save.";
                if (CalculationModelBlockedReason is string model) return "Creating is off. " + model;
                return null;
            }
        }

        public bool CanCreatePlans => CreateBlockedReason == null;

        /// <summary>
        /// Why QA plans can't be created or recalculated for the loaded clinical plan's calculation model (it must be the
        /// template phantom's model, Settings), or null. Demo mode skips the check until a model is set.
        /// </summary>
        public string? CalculationModelBlockedReason =>
            ClinicalPlan == null || (IsDemo && string.IsNullOrWhiteSpace(Settings.TemplateCalculationModel)) ? null
            : CalculationModelCheck.BlockedReason(Settings.TemplateCalculationModel, ClinicalPlan.CalculationModel);

        /// <summary>The department settings have no template phantom calculation model yet.</summary>
        public bool NeedsCalculationModelSetting => !IsDemo && string.IsNullOrWhiteSpace(Settings.TemplateCalculationModel);

        /// <summary>The department settings file has no template phantom yet (creating QA plans needs it).</summary>
        public bool NeedsTemplateSettings => !IsDemo && !Settings.HasTemplate;

        /// <summary>The department settings file exists but couldn't be read (path and reason); built-in defaults are in use.</summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSettingsError))] private string? _settingsError;

        public bool HasSettingsError => SettingsError != null;

        /// <summary>Settings need fixing before QA plans can be created (shows the "Open Settings" button).</summary>
        public bool NeedsSettingsAttention => HasSettingsError || NeedsTemplateSettings || NeedsCalculationModelSetting;

        partial void OnPatientChanged(PatientSummary? value) => NotifyCreateState();
        partial void OnClinicalPlanChanged(ClinicalPlanData? value) => NotifyCreateState();
        partial void OnQaCourseChanged(CourseSummary? value) => NotifyCreateState();
        partial void OnSettingsChanged(AppSettings value) => NotifyCreateState();
        partial void OnSettingsErrorChanged(string? value) => NotifyCreateState();

        private void NotifyCreateState()
        {
            OnPropertyChanged(nameof(CreateBlockedReason));
            OnPropertyChanged(nameof(CanCreatePlans));
            OnPropertyChanged(nameof(NeedsTemplateSettings));
            OnPropertyChanged(nameof(NeedsSettingsAttention));
            OnPropertyChanged(nameof(CalculationModelBlockedReason));
            OnPropertyChanged(nameof(NeedsCalculationModelSetting));
        }

        public void SetClinicalPlan(ClinicalPlanData plan)
        {
            ClinicalPlan = plan;
            Ptvs.Clear();
            QaPlans.Clear();
            _modelCache = null;
            int i = 0;
            foreach (var p in plan.Ptvs.Where(p => p.Mask != null && !p.Mask.IsEmpty))
                Ptvs.Add(new PtvRowViewModel(i++, p, Settings.HelperPatterns));
            PlanLoaded?.Invoke(this, EventArgs.Empty);
        }

        // ------------------------------------------------------------------ planning model cache
        private (string Key, PlanningModel Model, IReadOnlyList<PtvRowViewModel> Rows)? _modelCache;

        /// <summary>Planning model for the included PTVs with a positive Rx (rebuilt only when IDs or Rx change).</summary>
        public (PlanningModel Model, IReadOnlyList<PtvRowViewModel> Rows) GetModel()
        {
            var plan = ClinicalPlan ?? throw new InvalidOperationException("No clinical plan loaded.");
            if (plan.Dose == null) throw new InvalidOperationException("The clinical plan has no dose.");
            var rows = Ptvs.Where(r => r.Include).ToList();
            if (rows.Count == 0) throw new InvalidOperationException("No PTVs selected. Tick at least one PTV on the PTVs screen.");
            var bad = rows.Where(r => !(r.RxGy > 0)).Select(r => r.Id).ToList();
            if (bad.Count > 0) throw new InvalidOperationException($"Enter a prescription for: {string.Join(", ", bad)}.");
            string key = string.Join("|", rows.Select(r => r.Id + "=" + r.RxGy.ToString("R", CultureInfo.InvariantCulture)));
            if (_modelCache?.Key == key) return (_modelCache.Value.Model, _modelCache.Value.Rows);
            var model = new PlanningModel(rows.Select(r => r.ToTarget()).ToList(), plan.Dose);
            _modelCache = (key, model, rows);
            return (model, rows);
        }

        public OptimisationSettings OptimisationDefaults() => Settings.ToOptimisation();
    }
}
