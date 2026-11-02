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
    }

    /// <summary>
    /// Everything shared between screens for the open patient. Owned by the shell; screens read and update it.
    /// All members are used on the UI thread.
    /// </summary>
    public sealed partial class PlanningSession : ObservableObject
    {
        public PlanningSession(IPlanningService service, AppSettings settings, LaunchOptions launch, IDialogService dialogs)
        {
            Service = service; Settings = settings; Launch = launch; Dialogs = dialogs;
        }

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
