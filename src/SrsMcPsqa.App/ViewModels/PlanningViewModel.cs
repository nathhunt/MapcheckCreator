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
using SrsMcPsqa.Core.Services;
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

        public PlanningViewModel(ShellViewModel shell) : base(shell, "Array planning", SymbolRegular.Grid24, 3)
        {
            _alpha = shell.Session.Settings.Alpha;
            _preference = Preferences[0];
            Session.PlanLoaded += (_, __) => Clear();
            Session.PropertyChanged += (_, e) =>
            {
                NotifyCreateState();
                if (e.PropertyName == nameof(PlanningSession.Settings))
                    foreach (var p in Planes) p.Clearance.Update(p.IsoFromOrigin); // collision settings may have changed
            };
            Warnings.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasProblems));
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
        [ObservableProperty] private bool _optionsChanged;

        partial void OnPreferenceChanged(Choice<OrientationPreference> value) => OptionsChanged = HasPlanes;
        partial void OnAlphaChanged(double value) => OptionsChanged = HasPlanes;

        /// <summary>Page options: orientation preference and α. Edge margin, τ and N_min come from Settings.</summary>
        private OptimisationSettings CurrentSettings()
        {
            var s = Session.OptimisationDefaults();
            s.Alpha = Alpha;
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
        [ObservableProperty] private bool _show3D;
        [ObservableProperty] private bool _showDetails;
        [ObservableProperty] private bool _isComputing;
        [ObservableProperty] private string _resultSummary = "";

        /// <summary>Step size for the move buttons (mm).</summary>
        [ObservableProperty] private double _nudgeStep = 1.0;
        public static IReadOnlyList<double> NudgeSteps { get; } = new[] { 0.5, 1.0, 2.0, 5.0 };

        public bool HasPlanes => Planes.Count > 0;
        public bool HasProblems => Warnings.Count > 0;

        // ------------------------------------------------------------------ create-button state
        public bool CanCreate => HasPlanes && Session.CanCreatePlans;
        public string CreateButtonText => Planes.Count == 1 ? "Create 1 QA plan" : $"Create {Planes.Count} QA plans";
        [RelayCommand]
        private void OpenSettings() => Shell.Navigate(Shell.Settings);

        public string CreateHint => !HasPlanes
            ? "Waiting for the recommendation."
            : Session.CreateBlockedReason ?? "Creates the plans in Eclipse on the phantom and calculates them with the clinical MUs.";

        private void NotifyCreateState()
        {
            OnPropertyChanged(nameof(CanCreate));
            OnPropertyChanged(nameof(CreateButtonText));
            OnPropertyChanged(nameof(CreateHint));
        }

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

        /// <summary>Fills the "QA plan" column of every plane's PTV table: which plan(s) each PTV is assigned to.</summary>
        private void RefreshPlanColumn()
        {
            var planes = Planes.ToList();
            var byTarget = new Dictionary<int, List<PlanePlanViewModel>>();
            foreach (var p in planes)
                foreach (var k in p.AssignedIndices)
                {
                    if (!byTarget.TryGetValue(k, out var list)) byTarget[k] = list = new List<PlanePlanViewModel>();
                    list.Add(p);
                }
            foreach (var p in planes)
                foreach (var row in p.Metrics)
                {
                    var on = byTarget.TryGetValue(row.M.TargetIndex, out var list) ? list : new List<PlanePlanViewModel>();
                    row.PlanText = on.Count == 0 ? "none" : string.Join(", ", on.Select(x => x.Title));
                    row.IsOnThisPlan = on.Contains(p);
                }
        }

        private void Clear()
        {
            _model = null; _analysis = null; _rows = Array.Empty<PtvRowViewModel>();
            Planes.Clear(); Coverage.Clear(); Warnings.Clear(); ConstraintRows.Clear(); PtvHeaders.Clear();
            SelectedPlane = null; Scene = null; Overlay = null; Scene3D = null; OptionsChanged = false;
            ResultSummary = "";
            OnPropertyChanged(nameof(HasPlanes));
            NotifyCreateState();
        }

        /// <summary>Recommend automatically on arrival, and again if the PTV selection or prescriptions changed.</summary>
        public override void OnNavigatedTo()
        {
            if (!Session.HasPlan || IsComputing) return;
            PlanningModel current;
            try { current = Session.GetModel().Model; }
            catch (Exception ex) { ResultSummary = ex.Message; return; }
            if (!HasPlanes || !ReferenceEquals(current, _model)) _ = RecommendAsync();
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

            ResultSummary = "Finding the fewest QA plans...";
            IsComputing = true;
            var result = await Shell.ComputeAsync("Recommendation", () =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var analysis = new CandidateSearch(model, settings).Analyse();
                var rec = Recommender.Recommend(model, analysis);
                return Tuple.Create(analysis, rec, sw.Elapsed.TotalSeconds);
            });
            IsComputing = false;
            if (result == null) { ResultSummary = "Recommendation failed."; return; }
            _model = model; _rows = rows; _analysis = result.Item1;
            Apply(result.Item2, resetConstraints: true);
            ResultSummary = $"{result.Item2.Planes.Count} QA plan(s) needed for {rows.Count} PTV(s). Based on the clinical dose (before QA calculation).";
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
            foreach (var p in rec.Planes)
            {
                var vm = new PlanePlanViewModel(n++, p.Evaluation, _rows, Session.ClinicalPlan!.Isocentre, Session);
                vm.Updated += (_, __) => RefreshPlanColumn();
                Planes.Add(vm);
            }
            // Round small components of each plan's QA isocentre shift to zero (Settings: shift threshold).
            bool snapped = false;
            if (_model != null && _analysis != null)
                foreach (var p in Planes)
                {
                    var c = SnapCentre(p.Centre);
                    if ((c - p.Centre).Length < 1e-9) continue;
                    p.Update(_model.Evaluate(new ArrayPlane(p.Orientation, c), _analysis.Settings, _analysis.Reference));
                    snapped = true;
                }
            RefreshPlanColumn();
            if (snapped) { var re = Recommender.Build(_model!, _analysis!, Planes.Select(p => p.Evaluation.Plane), null, keepOrder: true); rec = re; }
            ApplyCoverage(rec);
            if (resetConstraints || ConstraintRows.Count != Planes.Count) ResetConstraints();
            OnPropertyChanged(nameof(HasPlanes));
            NotifyCreateState();
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
        /// <summary>Parameter "axis:sign", axis u/v/n (in-plane horizontal, in-plane vertical, normal); moves by sign × step.</summary>
        [RelayCommand]
        private void Nudge(string spec)
        {
            var p = SelectedPlane;
            if (p == null || _model == null) return;
            var parts = spec.Split(':');
            double amount = double.Parse(parts[1], CultureInfo.InvariantCulture) * NudgeStep;
            var o = p.Orientation;
            var delta = parts[0] switch
            {
                "u" => o.FromPlane(amount, 0, 0),
                "v" => o.FromPlane(0, amount, 0),
                _ => o.FromPlane(0, 0, amount),
            };
            var target = p.Centre + delta;
            // A step smaller than the shift threshold would be rounded straight back near zero: go to the threshold instead.
            double thr = Session.Settings.ShiftThresholdMm;
            if (thr > 0 && (SnapCentre(target) - p.Centre).Length < 1e-9)
            {
                var iso = Session.ClinicalPlan!.Isocentre;
                Vec3 off = iso - target, d = iso - p.Centre;
                double Bump(double t, double cur) => Math.Abs(t) < thr && Math.Abs(t - cur) > 1e-9 ? Math.Sign(t - cur) * thr : t;
                target = iso - new Vec3(Bump(off.X, d.X), Bump(off.Y, d.Y), Bump(off.Z, d.Z));
            }
            MovePlane(p, target, full: true);
        }

        /// <summary>Back to the recommended position for the selected plan.</summary>
        [RelayCommand]
        private void ResetPlane()
        {
            var p = SelectedPlane;
            if (p == null) return;
            MovePlane(p, p.RecommendedCentre, full: true);
            p.IsManual = false;
        }

        /// <summary>Opens the "Change plans" dialog (number of plans, orientations, required PTVs) and re-optimises on OK.</summary>
        [RelayCommand]
        private async Task ChangePlansAsync()
        {
            if (!HasPlanes) return;
            if (!Session.Dialogs.ShowChangePlans(this)) return;
            await ReoptimiseAsync();
        }

        /// <summary>Centre the intersected PTV cross-sections (of the PTVs on this plan) in the array, keeping the depth.</summary>
        [RelayCommand]
        private void CentreOnPtvs()
        {
            var p = SelectedPlane;
            if (p == null || _model == null) return;
            var o = p.Orientation;
            int j = _model.DepthIndexOf(o, p.Evaluation.Plane.Depth);
            var c = _model.CrossSectionCentre(p.AssignedIndices, o, j);
            if (c == null) { Session.Dialogs.ShowInfo("Centre on PTVs", "This plane doesn't intersect the plan's PTVs."); return; }
            double Snap(double x) => Math.Round(x * 2) / 2;
            MovePlane(p, o.FromPlane(Snap(c.Value.U), Snap(c.Value.V), p.Evaluation.Plane.Depth), full: true);
        }

        [RelayCommand]
        private void ApplyCentre()
        {
            var p = SelectedPlane;
            if (p == null) return;
            if (!double.TryParse(p.IsoX, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(p.IsoY, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(p.IsoZ, NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                Session.Dialogs.ShowInfo("QA isocentre", "Enter the isocentre position as three numbers in mm (use a decimal point).");
                return;
            }
            // Iso relative to the MapCHECK origin = clinical iso − array centre.
            MovePlane(p, p.ClinicalIso - new Vec3(x, y, z), full: true);
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

        /// <summary>Array centre with the small components of its QA isocentre shift rounded to zero.</summary>
        private Vec3 SnapCentre(Vec3 centre) =>
            ShiftRounding.SnapArrayCentre(Session.ClinicalPlan!.Isocentre, centre, Session.Settings.ShiftThresholdMm);

        private void MovePlane(PlanePlanViewModel p, Vec3 centre, bool full)
        {
            if (_model == null || _analysis == null) return;
            p.IsManual = true;
            if (full)
            {
                centre = SnapCentre(centre);
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
                Scene = PlaneRenderBuilder.BuildScene(o, depth, u, v, plan.Dose, targets, calculated: false, caption, Session.ReferenceGy);
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
            if (Session.CreateBlockedReason is string why) { Session.Dialogs.ShowInfo("Can't create QA plans yet", why); return; }
            var course = Session.QaCourse;
            if (course == null)
            {
                Session.Dialogs.ShowInfo("No QA course", "Choose the QA course on the Patient & plan screen.");
                return;
            }
            var settings = Session.Settings;
            if (!settings.HasTemplate && !Shell.IsFake)
            {
                Session.Dialogs.ShowInfo("Phantom not configured", "Set the template phantom patient, study and image IDs in Settings first.");
                return;
            }
            var planes = Planes.ToList();
            var list = string.Join(Environment.NewLine, planes.Select(p => $"  Plan {p.Number}: {p.OrientationText}, {p.DepthText}, PTVs: {string.Join(", ", p.AssignedIndices.Select(k => _rows[k].Id))}"));
            var clearanceWarnings = planes.Select(p => p.Clearance.WarningLine($"Plan {p.Number}")).Where(w => w != null).ToList();
            if (clearanceWarnings.Count > 0)
                list += $"{Environment.NewLine}{Environment.NewLine}Couch clearance warnings (plans are still created; check before measuring):{Environment.NewLine}" + string.Join(Environment.NewLine, clearanceWarnings);
            if (!Session.Dialogs.Confirm("Create QA plans",
                    $"Create {planes.Count} QA plan(s) in course '{course.Id}'{(course.WillBeCreated ? " (new course, created now)" : "")} and calculate dose with the clinical MUs?{Environment.NewLine}{Environment.NewLine}{list}",
                    "Create and calculate"))
                return;

            var rows = _rows;
            bool ok = await Shell.RunAsync($"Creating {planes.Count} QA plan(s)", async (pr, ct) =>
            {
                if (course.WillBeCreated)
                {
                    pr.AddLog($"Creating course '{course.Id}'...");
                    var created = await Session.Service.EnsureCourseAsync(course.Id, ct);
                    course = new CourseSummary { Id = created.Id };
                    Session.QaCourse = course;
                }

                // Name the plans first so the panel can list them ("Plan k of N") before anything is written.
                var existing = (await Session.Service.ListQaPlansAsync(course.Id, ct)).Select(x => x.PlanId)
                    .Concat(course.Plans.Select(x => x.PlanId)).Concat(Session.QaPlans.Select(q => q.PlanId)).ToList();
                var named = new List<(PlanePlanViewModel Plane, string Id)>();
                int n = 1;
                foreach (var p in planes)
                {
                    var id = QaPlanNaming.Generate(settings.PlanNamingPattern, n++, p.Orientation, existing);
                    existing.Add(id);
                    named.Add((p, id));
                }
                pr.ExpectPlans(named.Select(x => (x.Id, $"{x.Plane.OrientationText}, {x.Plane.AssignedIndices.Count} PTV{(x.Plane.AssignedIndices.Count == 1 ? "" : "s")}")));

                var orientations = planes.Select(p => p.Orientation).Distinct().ToList();
                pr.AddLog("Checking phantom images...");
                var phantoms = await Session.Service.EnsurePhantomImagesAsync(settings.ToTemplate(), orientations, pr.Reporter, ct, pr.Events);
                foreach (var ph in phantoms) Session.Phantoms[ph.Orientation] = ph;

                foreach (var (p, id) in named)
                {
                    ct.ThrowIfCancellationRequested();
                    var assigned = p.AssignedIndices.Select(k => rows[k]).ToList();
                    var req = QaPlanRequests.Create(p.Evaluation.Plane, plan.Isocentre, Session.Phantoms[p.Orientation], course.Id, id, assigned.Select(r => r.Id));
                    pr.AddLog($"{id}: create, copy beams, calculate");
                    QaPlanResult result;
                    try { result = await Session.Service.CreateQaPlanAsync(req, pr.Reporter, ct, pr.Events); }
                    catch { pr.CompletePlan(id, success: false); throw; }
                    pr.CompletePlan(id, result.CalculationSucceeded);
                    if (!result.CalculationSucceeded) pr.HasErrors = true;
                    foreach (var m in result.Messages) pr.AddLog($"{id}: {m}");
                    var qa = new QaPlanViewModel(Session, result, Session.Phantoms[p.Orientation], assigned, rows, _analysis);
                    Session.QaPlans.Add(qa);
                    await qa.RefreshAsync();
                }
            }, keepOpenOnSuccess: true);
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
