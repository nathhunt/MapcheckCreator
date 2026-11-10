using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SrsMcPsqa.App.Rendering;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Optimisation;
using SrsMcPsqa.Core.Services;
using Wpf.Ui.Controls;

namespace SrsMcPsqa.App.ViewModels
{
    /// <summary>A created and calculated QA plan.</summary>
    public sealed partial class QaPlanViewModel : ObservableObject
    {
        private readonly PlanningSession _session;
        private readonly OptimisationSettings _settings;
        private readonly double[]? _bestScores;

        public QaPlanViewModel(PlanningSession session, QaPlanResult result, PhantomImageInfo phantom,
            IReadOnlyList<PtvRowViewModel> assigned, IReadOnlyList<PtvRowViewModel> rows, CandidateAnalysis? analysis)
        {
            _session = session;
            _result = result;
            Phantom = phantom;
            AssignedRows = assigned;
            Rows = rows;
            _settings = (analysis?.Settings ?? session.OptimisationDefaults()).Clone();
            _settings.FineTuneInPlaneRangeMm = session.Settings.FineTuneInPlaneRangeMm;
            _settings.FineTuneNormalRangeMm = session.Settings.FineTuneNormalRangeMm;
            _bestScores = analysis?.Reference.BestScore;
            Clearance = new ClearanceViewModel(session);
            PreviewClearance = new ClearanceViewModel(session);
        }

        /// <summary>Gantry–couch clearance at the current position, and after the chosen move.</summary>
        public ClearanceViewModel Clearance { get; }
        public ClearanceViewModel PreviewClearance { get; }

        public PhantomImageInfo Phantom { get; }
        public PlanningSession Session => _session;

        /// <summary>Fine-tune suggestion for this plan (null until checked).</summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasFineTune), nameof(FineTuneBadge))] private FineTuneRowViewModel? _fineTune;
        public bool HasFineTune => FineTune != null;

        /// <summary>Short fine-tune state for the plan list.</summary>
        public string FineTuneBadge => FineTune == null ? (Iterations > 0 ? $"Fine-tuned ×{Iterations}" : "")
            : FineTune.WillMove ? "Shift pending: " + FineTune.ChosenMoveShort : "Keep current position";

        partial void OnIterationsChanged(int value) => OnPropertyChanged(nameof(FineTuneBadge));
        partial void OnFineTuneChanged(FineTuneRowViewModel? value) { ShowPreview = true; _ = UpdatePreviewAsync(); }
        public void NotifyFineTuneChanged() { OnPropertyChanged(nameof(FineTuneBadge)); _ = UpdatePreviewAsync(); }

        /// <summary>QA isocentre relative to the MapCHECK user origin (anatomical directions).</summary>
        public Vec3 IsoFromOrigin => Isocentre - Phantom.UserOrigin;
        public string IsoFromOriginText => ShiftLabels.Describe(IsoFromOrigin);

        /// <summary>QA isocentre relative to the MapCHECK user origin after the chosen fine-tune move (null when nothing moves).</summary>
        public Vec3? PreviewIsoFromOrigin => CanPreview && FineTune?.NewIsocentre(out _) is Vec3 iso ? iso - Phantom.UserOrigin : (Vec3?)null;
        public string StatusLine => CalcOk ? "✓ Calculated with the clinical MUs" : "✗ Calculation failed";
        public IReadOnlyList<PtvRowViewModel> AssignedRows { get; }
        public IReadOnlyList<PtvRowViewModel> Rows { get; }
        public ObservableCollection<MetricRowViewModel> Metrics { get; } = new ObservableCollection<MetricRowViewModel>();

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlanId), nameof(Isocentre), nameof(ArrayCentre), nameof(IsoText), nameof(ArrayText), nameof(CalcText), nameof(CalcOk), nameof(WarningsText), nameof(HasWarnings), nameof(IsoFromOriginText), nameof(IsoFromOrigin), nameof(StatusLine))]
        private QaPlanResult _result;
        [ObservableProperty] private bool _isChecked;
        [ObservableProperty] private int _iterations;
        [ObservableProperty] private PlaneScene? _scene;
        [ObservableProperty] private PlaneOverlay? _overlay;
        [ObservableProperty] private bool _showDiodes = true;

        public string PlanId => Result.PlanId;
        public string CourseId => Result.CourseId;
        public ArrayOrientation Orientation => Result.Orientation;
        public Vec3 Isocentre => Result.Isocentre;
        public Vec3 ClinicalIso => _session.ClinicalPlan!.Isocentre;

        /// <summary>QA dose is one fraction: compare with Rx per fraction.</summary>
        public int Fractions => Math.Max(1, _session.ClinicalPlan?.Fractions ?? 1);
        private double RxScale => 1.0 / Fractions;
        public string Header => $"{PlanId} · {Orientation.Label()}";
        public string AssignedText => string.Join(", ", AssignedRows.Select(r => r.Id));

        /// <summary>Patient point on the array centre.</summary>
        public Vec3 ArrayCentre => IsoGeometry.ArrayCentreInPatient(Phantom.UserOrigin, ClinicalIso, Isocentre);

        public string IsoText => "QA isocentre from MapCHECK user origin: " + IsoFromOriginText;
        public string ArrayText => "Array centre in patient: " + ArrayCentre;
        public bool CalcOk => Result.CalculationSucceeded && Result.Dose != null;
        public string CalcText => CalcOk ? "Dose calculated with the clinical MUs." : "Calculation failed: " + string.Join(" ", Result.Messages);

        /// <summary>Warnings Eclipse reported when calculating this plan (empty if none).</summary>
        public string WarningsText => string.Join(Environment.NewLine, Result.Messages.Where(m => m.StartsWith("Eclipse warning") || m.StartsWith("Eclipse error")));
        public bool HasWarnings => WarningsText.Length > 0;

        partial void OnShowDiodesChanged(bool value) => UpdateOverlay();

        // Calculated view (current position) and, when a move is chosen, an approximate preview after the move.
        private DoseGrid? _patientDose;
        private (PlaneScene Scene, List<MetricRowViewModel> Metrics, Vec3 Centre)? _current, _preview;
        private int _previewVersion;

        /// <summary>Show the preview of the chosen move instead of the calculated position.</summary>
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowingPreview), nameof(MetricsCaption), nameof(ShowCurrent))] private bool _showPreview = true;
        public bool ShowCurrent { get => !ShowPreview; set => ShowPreview = !value; }
        public bool CanPreview => FineTune?.WillMove == true;
        public bool ShowingPreview => ShowPreview && CanPreview && _preview != null;
        public string MetricsCaption => ShowingPreview
            ? "Predicted after the move (approximate: the calculated dose kept in place on the patient, array moved). Rx per fraction."
            : "PTVs on this plan, calculated. Rx is per fraction, to match the one-fraction QA dose.";

        partial void OnShowPreviewChanged(bool value) => ApplyDisplay();

        /// <summary>Recompute metrics and the 2D view from the calculated dose (in patient coordinates).</summary>
        public async Task RefreshAsync()
        {
            _preview = null;
            if (!CalcOk) { _current = null; _patientDose = null; Metrics.Clear(); Scene = null; Overlay = null; return; }
            Clearance.Update(IsoFromOrigin);
            _patientDose = QaPlanRequests.DoseInPatientFrame(Result.Dose!, Isocentre, ClinicalIso);
            _current = await BuildViewAsync(ArrayCentre, $"{PlanId} · {Orientation.Label()} · calculated QA dose on the array plane", null);
            await UpdatePreviewAsync();
        }

        private async Task<(PlaneScene, List<MetricRowViewModel>, Vec3)> BuildViewAsync(Vec3 centre, string caption, string? banner)
        {
            var dose = _patientDose!;
            var plane = new ArrayPlane(Orientation, centre);
            var rows = Rows;
            var settings = _settings;
            var best = _bestScores;
            double rxScale = RxScale;
            double? referenceGy = _session.ReferenceGy * rxScale; // QA dose is one fraction
            var built = await Task.Run(() =>
            {
                var targets = rows.Select(r => r.ToTarget(rxScale)).ToList();
                var model = new PlanningModel(targets, dose);
                var reference = best != null && best.Length == targets.Count ? new CoverageReference(best, new bool[targets.Count]) : null;
                var eval = model.Evaluate(plane, settings, reference);
                var scene = PlaneRenderBuilder.BuildScene(plane.Orientation, plane.Depth, plane.U, plane.V, dose,
                    rows.Select(r => (r.ToTarget(rxScale), r.Colour)).ToList(), calculated: banner == null, caption, referenceGy);
                scene.Banner = banner;
                return (eval, scene);
            });
            var metrics = built.eval.Targets.Where(t => t.AreaInSquare > 0 || AssignedRows.Any(a => a.Id == t.TargetId))
                .Select(m => new MetricRowViewModel(m, rows[m.TargetIndex].Brush)).ToList();
            return (built.scene, metrics, centre);
        }

        /// <summary>Rebuild the preview for the chosen fine-tune move (called when the choice or the custom move changes).</summary>
        public async Task UpdatePreviewAsync()
        {
            int version = ++_previewVersion;
            OnPropertyChanged(nameof(CanPreview));
            OnPropertyChanged(nameof(PreviewIsoFromOrigin));
            if (PreviewIsoFromOrigin is Vec3 previewIso) PreviewClearance.Update(previewIso);
            _preview = null;
            var iso = FineTune?.NewIsocentre(out _);
            if (_patientDose != null && CanPreview && iso != null)
            {
                var centre = IsoGeometry.ArrayCentreInPatient(Phantom.UserOrigin, ClinicalIso, iso.Value);
                var view = await BuildViewAsync(centre,
                    $"{PlanId} · {Orientation.Label()} · after moving the array {FineTune!.ChosenMoveShort}",
                    "PREVIEW (approximate: calculated dose, array moved; recalculate for the exact dose)");
                if (version != _previewVersion) return; // a newer change is being built
                _preview = view;
            }
            ApplyDisplay();
        }

        private void ApplyDisplay()
        {
            var v = ShowPreview && CanPreview && _preview != null ? _preview : _current;
            Metrics.Clear();
            if (v == null) { Scene = null; Overlay = null; }
            else
            {
                foreach (var m in v.Value.Metrics) Metrics.Add(m);
                Scene = v.Value.Scene;
                UpdateOverlay();
            }
            OnPropertyChanged(nameof(ShowingPreview));
            OnPropertyChanged(nameof(MetricsCaption));
        }

        private void UpdateOverlay()
        {
            var v = ShowPreview && CanPreview && _preview != null ? _preview : _current;
            if (Scene == null || v == null) { Overlay = null; return; }
            var c = v.Value.Centre;
            var overlay = PlaneRenderBuilder.BuildOverlay(Scene, Orientation.U(c), Orientation.V(c), _settings.UsableHalfWidth, ShowDiodes);
            // In the preview, outline where the array is now (the calculated position).
            if (ShowingPreview) overlay.GhostArray = (Orientation.U(ArrayCentre), Orientation.V(ArrayCentre));
            Overlay = overlay;
        }

        public FineTuneProposal Propose()
        {
            var targets = AssignedRows.Select(r => r.ToTarget(RxScale)).ToList();
            double[]? best = _bestScores == null ? null : AssignedRows.Select(r => Rows.ToList().IndexOf(r)).Select(i => i >= 0 ? _bestScores[i] : 0).ToArray();
            return FineTuneOptimizer.Propose(QaPlanRequests.FineTune(Result, ClinicalIso, Phantom, targets, best), _settings);
        }
    }

    public enum FineTuneDecision { Accept, Edit, Discard }

    /// <summary>One row of the fine-tune proposal table.</summary>
    public sealed partial class FineTuneRowViewModel : ObservableObject
    {
        public FineTuneRowViewModel(QaPlanViewModel plan, FineTuneProposal proposal)
        {
            Plan = plan; Proposal = proposal;
            var m = proposal.ArrayMoveRelativeToPatient;
            _customX = m.X.ToString("0.0#", CultureInfo.InvariantCulture);
            _customY = m.Y.ToString("0.0#", CultureInfo.InvariantCulture);
            _customZ = m.Z.ToString("0.0#", CultureInfo.InvariantCulture);
            _decision = proposal.Improves ? FineTuneDecision.Accept : FineTuneDecision.Discard;
        }

        public static IReadOnlyList<FineTuneDecision> Decisions { get; } = new[] { FineTuneDecision.Accept, FineTuneDecision.Edit, FineTuneDecision.Discard };

        public QaPlanViewModel Plan { get; }
        public FineTuneProposal Proposal { get; }

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResultText), nameof(IsEditing), nameof(UseSuggested), nameof(UseCustom), nameof(KeepCurrent), nameof(WillMove), nameof(ChosenMoveShort))]
        private FineTuneDecision _decision;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResultText), nameof(WillMove), nameof(ChosenMoveShort))] private string _customX;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResultText), nameof(WillMove), nameof(ChosenMoveShort))] private string _customY;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResultText), nameof(WillMove), nameof(ChosenMoveShort))] private string _customZ;

        partial void OnDecisionChanged(FineTuneDecision value) => Plan.NotifyFineTuneChanged();
        partial void OnCustomXChanged(string value) => Plan.NotifyFineTuneChanged();
        partial void OnCustomYChanged(string value) => Plan.NotifyFineTuneChanged();
        partial void OnCustomZChanged(string value) => Plan.NotifyFineTuneChanged();

        // Radio buttons.
        public bool UseSuggested { get => Decision == FineTuneDecision.Accept; set { if (value) Decision = FineTuneDecision.Accept; } }
        public bool UseCustom { get => Decision == FineTuneDecision.Edit; set { if (value) Decision = FineTuneDecision.Edit; } }
        public bool KeepCurrent { get => Decision == FineTuneDecision.Discard; set { if (value) Decision = FineTuneDecision.Discard; } }

        public bool HasSuggestion => Proposal.Improves && Proposal.ArrayMoveRelativeToPatient.Length >= 0.05;

        /// <summary>True when applying would move this plan's isocentre.</summary>
        public bool WillMove
        {
            get
            {
                var iso = NewIsocentre(out var error);
                return error == null && iso != null && (iso.Value - Plan.Isocentre).Length > 1e-6;
            }
        }

        /// <summary>The chosen array move, e.g. "Left 0.3 mm, Ant 0.0 mm, Sup 0.0 mm".</summary>
        public string ChosenMoveShort
        {
            get
            {
                var iso = NewIsocentre(out var error);
                if (error != null || iso == null) return "";
                return ShiftLabels.Describe(IsoGeometry.ArrayCentreInPatient(Plan.Phantom.UserOrigin, Plan.ClinicalIso, iso.Value) - Plan.ArrayCentre);
            }
        }

        public string SuggestionHeadline => HasSuggestion
            ? "Suggested: move the array from its current position " + Proposal.ArrayMoveDescription
            : "No better position found. The current position is the best the search found.";

        /// <summary>Predicted change per PTV, one line each.</summary>
        public IReadOnlyList<string> PredictedLines => Proposal.Predicted.Targets.Zip(Proposal.Current.Targets, (p, c) =>
            string.Format(CultureInfo.InvariantCulture, "{0}: diodes ≥ Rx {1} → {2}, J {3:0.000} → {4:0.000}{5}",
                p.TargetId, c.DiodesAtOrAboveRx, p.DiodesAtOrAboveRx, c.Score, p.Score, p.Clipped ? " (clipped)" : "")).ToList();

        public bool IsEditing => Decision == FineTuneDecision.Edit;
        public string PlanId => Plan.PlanId;
        public string CurrentText => Plan.ArrayCentre.ToString();

        public string ProposedText => Proposal.ArrayMoveRelativeToPatient.Length < 0.05 ? "No change suggested" : Proposal.ArrayMoveDescription;

        public string PredictedText
        {
            get
            {
                var parts = Proposal.Predicted.Targets.Zip(Proposal.Current.Targets, (p, c) =>
                    string.Format(CultureInfo.InvariantCulture, "{0}: J {1:0.000}→{2:0.000}, ≥Rx diodes {3}→{4}{5}",
                        p.TargetId, c.Score, p.Score, c.DiodesAtOrAboveRx, p.DiodesAtOrAboveRx, p.Clipped ? " (clipped)" : ""));
                return string.Format(CultureInfo.InvariantCulture, "ΣJ {0:0.000} → {1:0.000} (predicted)\n", Proposal.CurrentObjective, Proposal.PredictedObjective)
                       + string.Join("\n", parts);
            }
        }

        /// <summary>New QA isocentre for this row's decision, or null for Discard / invalid input.</summary>
        public Vec3? NewIsocentre(out string? error)
        {
            error = null;
            switch (Decision)
            {
                case FineTuneDecision.Accept:
                    return Proposal.ProposedQaIsocentre;
                case FineTuneDecision.Edit:
                    if (!TryParse(CustomX, out var x) || !TryParse(CustomY, out var y) || !TryParse(CustomZ, out var z))
                    {
                        error = $"{PlanId}: enter the array move as numbers in mm.";
                        return null;
                    }
                    var move = new Vec3(x, y, z);
                    if (Math.Abs(x) > 50 || Math.Abs(y) > 50 || Math.Abs(z) > 50)
                    {
                        error = $"{PlanId}: moves larger than 50 mm are not allowed here.";
                        return null;
                    }
                    // Array moves by m relative to the patient → isocentre moves by −m.
                    return Plan.Isocentre + IsoGeometry.IsoShiftForArrayMove(Plan.ArrayCentre, Plan.ArrayCentre + move);
                default:
                    return null;
            }
        }

        public string ResultText
        {
            get
            {
                var iso = NewIsocentre(out var error);
                if (error != null) return error;
                if (iso == null || (iso.Value - Plan.Isocentre).Length <= 1e-6) return "The plan stays as it is.";
                return "New QA isocentre from MapCHECK origin: " + ShiftLabels.Describe(iso.Value - Plan.Phantom.UserOrigin);
            }
        }

        private static bool TryParse(string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>Screen 4: calculated QA plans, fine-tuning, deletion.</summary>
    public sealed partial class ReviewViewModel : PageViewModel
    {
        public ReviewViewModel(ShellViewModel shell) : base(shell, "Review & fine-tune", SymbolRegular.CheckmarkCircle24, 4)
        {
            QaPlans.CollectionChanged += (_, __) => NotifyFooter();
        }

        public ObservableCollection<QaPlanViewModel> QaPlans => Session.QaPlans;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection))] private QaPlanViewModel? _selectedQaPlan;
        [ObservableProperty] private bool _isChecking;

        public bool HasSelection => SelectedQaPlan != null;

        /// <summary>Plans whose chosen fine-tune option moves the isocentre.</summary>
        public IReadOnlyList<QaPlanViewModel> PendingPlans => QaPlans.Where(p => p.FineTune?.WillMove == true).ToList();
        public int PendingCount => PendingPlans.Count;
        public bool HasPending => PendingCount > 0;
        public bool AnyChecked => QaPlans.Any(p => p.HasFineTune);

        public string ApplyButtonText => PendingCount == 1 ? "Move isocentre and recalculate 1 plan" : $"Move isocentres and recalculate {PendingCount} plans";

        public string FooterHint => QaPlans.Count == 0 ? "No QA plans yet."
            : HasPending ? $"{PendingCount} plan(s) will be moved: " + string.Join(", ", PendingPlans.Select(p => p.PlanId)) + ". Plans set to 'Keep' stay as they are."
            : AnyChecked ? "No moves chosen. Select a plan to review its suggestion, or check again."
            : "Optional: check whether moving the array would measure the PTVs better.";

        private void NotifyFooter()
        {
            foreach (var p in new[] { nameof(PendingPlans), nameof(PendingCount), nameof(HasPending), nameof(AnyChecked), nameof(ApplyButtonText), nameof(FooterHint) })
                OnPropertyChanged(p);
        }

        public override void OnNavigatedTo()
        {
            if (SelectedQaPlan == null || !QaPlans.Contains(SelectedQaPlan)) SelectedQaPlan = QaPlans.FirstOrDefault();
            NotifyFooter();
        }

        [RelayCommand]
        private Task CheckSelectedAsync() => SelectedQaPlan == null ? Task.CompletedTask : CheckAsync(new[] { SelectedQaPlan });

        [RelayCommand]
        private Task CheckAllAsync() => CheckAsync(QaPlans.ToList());

        private async Task CheckAsync(IReadOnlyList<QaPlanViewModel> candidates)
        {
            var plans = candidates.Where(p => p.CalcOk).ToList();
            if (plans.Count == 0) return;
            IsChecking = true;
            try
            {
                var proposals = await Shell.ComputeAsync("Fine-tune", () => plans.Select(p => (p, prop: p.Propose())).ToList());
                if (proposals == null) return;
                foreach (var (p, prop) in proposals)
                {
                    var row = new FineTuneRowViewModel(p, prop);
                    row.PropertyChanged += (_, __) => NotifyFooter();
                    p.FineTune = row;
                }
            }
            finally
            {
                IsChecking = false;
                NotifyFooter();
            }
        }

        [RelayCommand]
        private async Task ApplyAsync()
        {
            var moves = new List<(QaPlanViewModel Plan, Vec3 Iso)>();
            foreach (var p in QaPlans.Where(p => p.FineTune != null))
            {
                var iso = p.FineTune!.NewIsocentre(out var error);
                if (error != null) { SelectedQaPlan = p; Session.Dialogs.ShowInfo("Check the array move", error); return; }
                if (iso != null && (iso.Value - p.Isocentre).Length > 1e-6) moves.Add((p, iso.Value));
            }
            if (moves.Count == 0) { Session.Dialogs.ShowInfo("Nothing to apply", "No plan is set to move. Choose 'Use the suggested move' or 'Enter my own move' for a plan first."); return; }
            var list = string.Join(Environment.NewLine, moves.Select(m => $"  {m.Plan.PlanId}: array {m.Plan.FineTune!.ChosenMoveShort}"));
            var clearanceWarnings = moves.Select(m => m.Plan.PreviewClearance.WarningLine(m.Plan.PlanId)).Where(w => w != null).ToList();
            if (clearanceWarnings.Count > 0)
                list += $"{Environment.NewLine}{Environment.NewLine}Couch clearance warnings after the move (not blocking):{Environment.NewLine}" + string.Join(Environment.NewLine, clearanceWarnings);
            if (!Session.Dialogs.Confirm("Move isocentres",
                    $"Move the isocentre and recalculate {moves.Count} QA plan(s)?{Environment.NewLine}{Environment.NewLine}{list}", "Move and recalculate")) return;

            await Shell.RunAsync($"Moving isocentre and recalculating {moves.Count} plan(s)", async (pr, ct) =>
            {
                pr.ExpectPlans(moves.Select(m => (m.Plan.PlanId, "move isocentre and recalculate")));
                foreach (var (plan, iso) in moves)
                {
                    ct.ThrowIfCancellationRequested();
                    pr.AddLog($"{plan.PlanId}: move isocentre, recalculate");
                    var region = QaPlanRequests.DoseRegion(plan.Orientation, plan.Phantom.UserOrigin);
                    QaPlanResult result;
                    try { result = await Session.Service.MoveIsocentreAsync(plan.CourseId, plan.PlanId, iso, region, pr.Reporter, ct, pr.Events); }
                    catch { pr.CompletePlan(plan.PlanId, success: false); throw; }
                    pr.CompletePlan(plan.PlanId, result.CalculationSucceeded);
                    if (!result.CalculationSucceeded) pr.HasErrors = true;
                    plan.Result = result;
                    plan.Iterations++;
                    plan.FineTune = null;
                    await plan.RefreshAsync();
                }
            }, keepOpenOnSuccess: true);
            NotifyFooter();
        }

        [RelayCommand]
        private Task DeleteSelectedAsync() => SelectedQaPlan == null ? Task.CompletedTask : DeleteAsync(new List<QaPlanViewModel> { SelectedQaPlan });

        [RelayCommand]
        private Task DeleteAllAsync() => DeleteAsync(QaPlans.ToList());

        private async Task DeleteAsync(List<QaPlanViewModel> plans)
        {
            if (plans.Count == 0) return;
            if (!Session.Dialogs.Confirm("Delete QA plans",
                    $"Delete {plans.Count} QA plan(s) from Eclipse?\n\n{string.Join("\n", plans.Select(p => "  " + p.PlanId))}\n\nOnly plans created by this app (tagged) can be deleted. Phantom images are kept.",
                    "Delete", danger: true))
                return;
            await Shell.RunAsync("Deleting QA plans", async (pr, ct) =>
            {
                foreach (var g in plans.GroupBy(p => p.CourseId))
                {
                    pr.BeginStep($"Deleting {g.Count()} plan(s) from course {g.Key}");
                    await Session.Service.DeleteQaPlansAsync(g.Key, g.Select(p => p.PlanId).ToList(), pr.Reporter, ct);
                    foreach (var p in g) Session.QaPlans.Remove(p);
                }
            });
            SelectedQaPlan = QaPlans.FirstOrDefault();
            NotifyFooter();
            if (QaPlans.Count == 0) Shell.Navigate(Shell.Planning);
        }

        [RelayCommand]
        private void AddNew()
        {
            var covered = new HashSet<string>(QaPlans.SelectMany(p => p.AssignedRows.Select(r => r.Id)));
            Shell.Planning.PrepareForNewPlans(covered);
            Shell.Navigate(Shell.Ptvs);
        }
    }
}
