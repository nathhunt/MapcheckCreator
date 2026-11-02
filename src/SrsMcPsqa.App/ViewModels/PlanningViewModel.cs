using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SrsMcPsqa.App.Controls;
using SrsMcPsqa.App.Rendering;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Optimisation;
using Wpf.Ui.Controls;

namespace SrsMcPsqa.App.ViewModels
{
    /// <summary>
    /// Screen 3: recommendation, constrained re-optimisation, manual adjustment and QA plan creation.
    /// All values here are approximations based on the clinical dose.
    /// </summary>
    public sealed partial class PlanningViewModel : PageViewModel
    {
        private PlanningModel? _model;
        private IReadOnlyList<PtvRowViewModel> _rows = Array.Empty<PtvRowViewModel>();
        private CandidateAnalysis? _analysis;
        private Vec3? _dragStart;

        public PlanningViewModel(ShellViewModel shell) : base(shell, "Array planning", SymbolRegular.Grid24)
        {
            var d = shell.Session.Settings;
            _alpha = d.Alpha; _edgeMargin = d.EdgeMarginMm; _tau = d.Tau; _minDiodes = d.MinDiodes;
            _preference = Preferences[0];
            Session.PlanLoaded += (_, __) => Clear();
        }

        public static IReadOnlyList<Choice<OrientationPreference>> Preferences { get; } = new[]
        {
            new Choice<OrientationPreference>(OrientationPreference.NoPreference, "No preference"),
            new Choice<OrientationPreference>(OrientationPreference.PreferSame, "Prefer same orientation for all plans"),
            new Choice<OrientationPreference>(OrientationPreference.Prefer0, "Prefer 0°"),
            new Choice<OrientationPreference>(OrientationPreference.Prefer270, "Prefer 270°"),
            new Choice<OrientationPreference>(OrientationPreference.Only0, "Only 0°"),
            new Choice<OrientationPreference>(OrientationPreference.Only270, "Only 270°"),
        };

        // ------------------------------------------------------------------ options
        [ObservableProperty] private Choice<OrientationPreference> _preference;
        [ObservableProperty] private double _alpha;
        [ObservableProperty] private double _edgeMargin;
        [ObservableProperty] private double _tau;
        [ObservableProperty] private int _minDiodes;
        [ObservableProperty] private bool _optionsChanged;

        partial void OnPreferenceChanged(Choice<OrientationPreference> value) => OptionsChanged = HasPlanes;
        partial void OnAlphaChanged(double value) => OptionsChanged = HasPlanes;
        partial void OnEdgeMarginChanged(double value) => OptionsChanged = HasPlanes;
        partial void OnTauChanged(double value) => OptionsChanged = HasPlanes;
        partial void OnMinDiodesChanged(int value) => OptionsChanged = HasPlanes;

        private OptimisationSettings CurrentSettings()
        {
            var s = Session.OptimisationDefaults();
            s.Alpha = Alpha; s.EdgeMarginMm = EdgeMargin; s.Tau = Tau; s.MinDiodes = MinDiodes;
            s.OrientationPreference = Preference.Value;
            s.Validate();
            return s;
        }

        // ------------------------------------------------------------------ results
        public ObservableCollection<PlanePlanViewModel> Planes { get; } = new ObservableCollection<PlanePlanViewModel>();
        public ObservableCollection<CoverageRowViewModel> Coverage { get; } = new ObservableCollection<CoverageRowViewModel>();
        public ObservableCollection<string> Warnings { get; } = new ObservableCollection<string>();
        public ObservableCollection<ConstraintRowViewModel> ConstraintRows { get; } = new ObservableCollection<ConstraintRowViewModel>();
        public ObservableCollection<string> PtvHeaders { get; } = new ObservableCollection<string>();

        [ObservableProperty] private PlanePlanViewModel? _selectedPlane;
        [ObservableProperty] private int _numberOfPlans = 1;
        [ObservableProperty] private PlaneScene? _scene;
        [ObservableProperty] private PlaneOverlay? _overlay;
        [ObservableProperty] private Scene3D? _scene3D;
        [ObservableProperty] private bool _showDiodes = true;
        [ObservableProperty] private string _resultSummary = "Click Recommend to compute the array planes.";

        public bool HasPlanes => Planes.Count > 0;

        partial void OnSelectedPlaneChanged(PlanePlanViewModel? value) { UpdatePlaneView(force: true); Update3D(); }
        partial void OnShowDiodesChanged(bool value) { UpdatePlaneView(force: false); Update3D(); }

        private bool _syncingRows;

        partial void OnNumberOfPlansChanged(int value)
        {
            if (_syncingRows) return;
            if (value < 1) { NumberOfPlans = 1; return; }
            while (ConstraintRows.Count < value) ConstraintRows.Add(NewConstraintRow(ConstraintRows.Count + 1));
            while (ConstraintRows.Count > value) ConstraintRows.RemoveAt(ConstraintRows.Count - 1);
        }

        private void Clear()
        {
            _model = null; _analysis = null; _rows = Array.Empty<PtvRowViewModel>();
            Planes.Clear(); Coverage.Clear(); Warnings.Clear(); ConstraintRows.Clear(); PtvHeaders.Clear();
            SelectedPlane = null; Scene = null; Overlay = null; Scene3D = null; OptionsChanged = false;
            ResultSummary = "Click Recommend to compute the array planes.";
            OnPropertyChanged(nameof(HasPlanes));
        }

        public override void OnNavigatedTo()
        {
            if (!HasPlanes) Update3D();
        }

        // ------------------------------------------------------------------ recommend / re-optimise
        [RelayCommand]
        private async Task RecommendAsync()
        {
            OptimisationSettings settings;
            PlanningModel model;
            IReadOnlyList<PtvRowViewModel> rows;
            try
            {
                settings = CurrentSettings();
                (model, rows) = Session.GetModel();
            }
            catch (Exception ex)
            {
                Session.Dialogs.ShowError("Cannot recommend", ex.Message);
                return;
            }

            ResultSummary = "Computing...";
            var result = await Shell.ComputeAsync("Recommendation", () =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var analysis = new CandidateSearch(model, settings).Analyse();
                var rec = Recommender.Recommend(model, analysis);
                return Tuple.Create(analysis, rec, sw.Elapsed.TotalSeconds);
            });
            if (result == null) { ResultSummary = "Recommendation failed."; return; }
            _model = model; _rows = rows; _analysis = result.Item1;
            Apply(result.Item2, resetConstraints: true);
            ResultSummary = $"{result.Item2.Planes.Count} plan(s) recommended in {result.Item3:0.0} s (approximation from the clinical dose).";
            OptionsChanged = false;
        }

        [RelayCommand]
        private async Task ReoptimiseAsync()
        {
            if (_model == null || _analysis == null) { await RecommendAsync(); return; }
            var model = _model; var analysis = _analysis;
            var constraints = ConstraintRows.Select(r => new PlaneConstraint(r.Orientation.Value,
                r.Cells.Where(c => c.IsAssigned).Select(c => c.TargetIndex))).ToList();
            ResultSummary = "Re-optimising...";
            var rec = await Shell.ComputeAsync("Re-optimisation", () => ConstrainedOptimizer.Optimise(model, analysis, constraints));
            if (rec == null) return;
            Apply(rec, resetConstraints: false);
            ResultSummary = $"Re-optimised with your constraints: {rec.Planes.Count} plan(s).";
        }

        [RelayCommand]
        private void ClearAssignments()
        {
            foreach (var r in ConstraintRows)
            {
                r.Orientation = ConstraintRowViewModel.OrientationChoices[0];
                foreach (var c in r.Cells) c.IsAssigned = false;
            }
        }

        private void Apply(Recommendation rec, bool resetConstraints)
        {
            Planes.Clear();
            int n = 1;
            foreach (var p in rec.Planes) Planes.Add(new PlanePlanViewModel(n++, p.Evaluation, _rows));
            ApplyCoverage(rec);
            if (resetConstraints || ConstraintRows.Count != Planes.Count) ResetConstraints();
            OnPropertyChanged(nameof(HasPlanes));
            SelectedPlane = Planes.FirstOrDefault();
        }

        private void ApplyCoverage(Recommendation rec)
        {
            Coverage.Clear();
            foreach (var t in rec.Targets)
                Coverage.Add(new CoverageRowViewModel(t.TargetId, _rows[t.TargetIndex].Brush, t.Status,
                    t.PrimaryPlane is int i ? $"plan {i + 1}" : "–"));
            Warnings.Clear();
            foreach (var w in rec.Warnings) Warnings.Add(w);
        }

        private void ResetConstraints()
        {
            PtvHeaders.Clear();
            foreach (var r in _rows) PtvHeaders.Add(r.Id);
            ConstraintRows.Clear();
            foreach (var p in Planes)
            {
                var row = NewConstraintRow(p.Number);
                row.Orientation = ConstraintRowViewModel.OrientationChoices.First(c => c.Value == p.Orientation);
                foreach (var c in row.Cells) c.IsAssigned = p.Evaluation.Targets[c.TargetIndex].Covered;
                ConstraintRows.Add(row);
            }
            _syncingRows = true;
            NumberOfPlans = Math.Max(1, Planes.Count);
            _syncingRows = false;
        }

        private ConstraintRowViewModel NewConstraintRow(int number) =>
            new ConstraintRowViewModel(number, _rows.Select((r, k) => new AssignmentCellViewModel(k, r.Id)));

        /// <summary>Re-evaluate all planes at their current centres (after manual edits) and refresh coverage.</summary>
        private void Reevaluate()
        {
            if (_model == null || _analysis == null) return;
            var rec = Recommender.Build(_model, _analysis, Planes.Select(p => p.Evaluation.Plane), null, keepOrder: true);
            for (int i = 0; i < Planes.Count; i++) Planes[i].Update(rec.Planes[i].Evaluation);
            ApplyCoverage(rec);
            UpdatePlaneView(force: false);
            Update3D();
        }

        // ------------------------------------------------------------------ manual adjustment
        /// <summary>Parameter "axis:amount", axis u/v/n (in-plane horizontal, in-plane vertical, normal).</summary>
        [RelayCommand]
        private void Nudge(string spec)
        {
            var p = SelectedPlane;
            if (p == null || _model == null) return;
            var parts = spec.Split(':');
            double amount = double.Parse(parts[1], CultureInfo.InvariantCulture);
            var o = p.Orientation;
            var delta = parts[0] switch
            {
                "u" => o.FromPlane(amount, 0, 0),
                "v" => o.FromPlane(0, amount, 0),
                _ => o.FromPlane(0, 0, amount),
            };
            MovePlane(p, p.Centre + delta, full: true);
        }

        [RelayCommand]
        private void ApplyCentre()
        {
            var p = SelectedPlane;
            if (p == null) return;
            if (!double.TryParse(p.CentreX, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(p.CentreY, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(p.CentreZ, NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                Session.Dialogs.ShowInfo("Array centre", "Enter the centre as three numbers in mm (use a decimal point).");
                return;
            }
            MovePlane(p, new Vec3(x, y, z), full: true);
        }

        [RelayCommand]
        private void Drag(PlaneDragArgs args)
        {
            var p = SelectedPlane;
            if (p == null) return;
            _dragStart ??= p.Centre;
            var target = _dragStart.Value + p.Orientation.FromPlane(args.DU, args.DV, 0);
            MovePlane(p, target, full: args.Completed);
            if (args.Completed) _dragStart = null;
        }

        private void MovePlane(PlanePlanViewModel p, Vec3 centre, bool full)
        {
            if (_model == null || _analysis == null) return;
            p.IsManual = true;
            if (full)
            {
                p.Update(_model.Evaluate(new ArrayPlane(p.Orientation, centre), _analysis.Settings, _analysis.Reference));
                Reevaluate();
            }
            else
            {
                // Live drag: only the dragged plane and its overlay.
                p.Update(_model.Evaluate(new ArrayPlane(p.Orientation, centre), _analysis.Settings, _analysis.Reference));
                UpdatePlaneView(force: false);
            }
        }

        // ------------------------------------------------------------------ views
        private void UpdatePlaneView(bool force)
        {
            var p = SelectedPlane;
            var plan = Session.ClinicalPlan;
            if (p == null || plan == null || _analysis == null) { Scene = null; Overlay = null; return; }
            var o = p.Orientation;
            double u = o.U(p.Centre), v = o.V(p.Centre), depth = p.Evaluation.Plane.Depth;
            var s = Scene;
            bool rebuild = force || s == null || s.Orientation != o || Math.Abs(s.Depth - depth) > 1e-6
                           || Math.Abs(s.ViewCentreU - u) > 15 || Math.Abs(s.ViewCentreV - v) > 15;
            if (rebuild)
            {
                var targets = _rows.Select(r => (r.ToTarget(), r.Colour)).ToList();
                var caption = $"Plan {p.Number} · {o.Label()} · {p.DepthText}";
                Scene = PlaneRenderBuilder.BuildScene(o, depth, u, v, plan.Dose, targets, calculated: false, caption);
            }
            Overlay = PlaneRenderBuilder.BuildOverlay(Scene, u, v, _analysis.Settings.UsableHalfWidth, ShowDiodes);
        }

        private void Update3D()
        {
            var meshes = Session.Ptvs.Where(r => r.Info.Mesh != null && (r.Include || _rows.Contains(r)))
                .Select(r => (r.Id, r.Info.Mesh!, r.Colour)).ToList();
            var planes = Planes.Select(p => (p.Orientation, p.Centre, p == SelectedPlane)).ToList();
            Scene3D = new Scene3D { Meshes = meshes, Planes = planes, ShowDiodes = ShowDiodes };
        }

        // ------------------------------------------------------------------ create QA plans
        [RelayCommand]
        private async Task CreateQaPlansAsync()
        {
            var plan = Session.ClinicalPlan;
            if (plan == null || Planes.Count == 0) return;
            var course = Session.QaCourse;
            if (course == null)
            {
                Session.Dialogs.ShowInfo("No QA course", "Choose the QA course on the Patient & plan screen. Create it in Eclipse first if needed.");
                return;
            }
            var settings = Session.Settings;
            if (!settings.HasTemplate && !Shell.IsFake)
            {
                Session.Dialogs.ShowInfo("Phantom not configured", "Set the template phantom patient and image IDs in Settings first.");
                return;
            }
            var planes = Planes.ToList();
            var list = string.Join(Environment.NewLine, planes.Select(p => $"  Plan {p.Number}: {p.OrientationText}, {p.DepthText}, PTVs: {string.Join(", ", p.AssignedIndices.Select(k => _rows[k].Id))}"));
            if (!Session.Dialogs.Confirm("Create QA plans",
                    $"Create {planes.Count} QA plan(s) in course '{course.Id}' and calculate dose with the clinical MUs?{Environment.NewLine}{Environment.NewLine}{list}",
                    "Create and calculate"))
                return;

            var rows = _rows;
            bool ok = await Shell.RunAsync($"Creating {planes.Count} QA plan(s)", async (pr, ct) =>
            {
                var orientations = planes.Select(p => p.Orientation).Distinct().ToList();
                var step = pr.BeginStep("Checking phantom images...");
                var phantoms = await Session.Service.EnsurePhantomImagesAsync(settings.ToTemplate(), orientations, pr.Reporter, ct);
                foreach (var ph in phantoms) Session.Phantoms[ph.Orientation] = ph;
                step.State = "Done";

                var existing = (await Session.Service.ListQaPlansAsync(course.Id, ct)).Select(x => x.PlanId)
                    .Concat(course.Plans.Select(x => x.PlanId)).Concat(Session.QaPlans.Select(q => q.PlanId)).ToList();
                int n = 1;
                foreach (var p in planes)
                {
                    ct.ThrowIfCancellationRequested();
                    var id = QaPlanNaming.Generate(settings.PlanNamingPattern, n++, p.Orientation, existing);
                    existing.Add(id);
                    var assigned = p.AssignedIndices.Select(k => rows[k]).ToList();
                    var req = QaPlanRequests.Create(p.Evaluation.Plane, plan.Isocentre, Session.Phantoms[p.Orientation], course.Id, id, assigned.Select(r => r.Id));
                    var s = pr.BeginStep($"{id}: create, copy beams, calculate");
                    var result = await Session.Service.CreateQaPlanAsync(req, pr.Reporter, ct);
                    s.State = result.CalculationSucceeded ? "Done" : "Failed";
                    if (!result.CalculationSucceeded) pr.HasErrors = true;
                    foreach (var m in result.Messages) pr.AddLog($"{id}: {m}");
                    var qa = new QaPlanViewModel(Session, result, Session.Phantoms[p.Orientation], assigned, rows, _analysis);
                    Session.QaPlans.Add(qa);
                    await qa.RefreshAsync();
                }
            }, keepOpenOnSuccess: false);
            if (Session.QaPlans.Count > 0) Shell.Navigate(Shell.Review);
            if (!ok) Log("Some QA plans were not created; see the progress log.");
        }

        private static void Log(string m) => Infrastructure.Log.Warn(m);

        /// <summary>Called by Review → "Add new QA plans": pre-select PTVs not yet on any QA plan.</summary>
        public void PrepareForNewPlans(ISet<string> alreadyCoveredIds)
        {
            foreach (var r in Session.Ptvs) r.Include = !alreadyCoveredIds.Contains(r.Id);
            Clear();
            ResultSummary = "PTVs not yet on a QA plan are pre-selected (change them on the PTVs screen). Click Recommend.";
        }
    }
}
