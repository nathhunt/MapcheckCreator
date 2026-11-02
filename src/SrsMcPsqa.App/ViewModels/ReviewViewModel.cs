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
        }

        public PhantomImageInfo Phantom { get; }
        public IReadOnlyList<PtvRowViewModel> AssignedRows { get; }
        public IReadOnlyList<PtvRowViewModel> Rows { get; }
        public ObservableCollection<MetricRowViewModel> Metrics { get; } = new ObservableCollection<MetricRowViewModel>();

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlanId), nameof(Isocentre), nameof(ArrayCentre), nameof(IsoText), nameof(ArrayText), nameof(CalcText), nameof(CalcOk))]
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
        public string Header => $"{PlanId} ({Orientation.Label()})";
        public string AssignedText => string.Join(", ", AssignedRows.Select(r => r.Id));

        /// <summary>Patient point on the array centre.</summary>
        public Vec3 ArrayCentre => IsoGeometry.ArrayCentreInPatient(Phantom.UserOrigin, ClinicalIso, Isocentre);

        public string IsoText => "Isocentre relative to phantom user origin: " + (Isocentre - Phantom.UserOrigin);
        public string ArrayText => "Array centre in patient: " + ArrayCentre;
        public bool CalcOk => Result.CalculationSucceeded && Result.Dose != null;
        public string CalcText => CalcOk ? "Dose calculated with the clinical MUs." : "Calculation failed: " + string.Join(" ", Result.Messages);

        partial void OnShowDiodesChanged(bool value) => UpdateOverlay();

        /// <summary>Recompute metrics and the 2D view from the calculated dose (in patient coordinates).</summary>
        public async Task RefreshAsync()
        {
            if (!CalcOk) { Metrics.Clear(); Scene = null; Overlay = null; return; }
            var dose = QaPlanRequests.DoseInPatientFrame(Result.Dose!, Isocentre, ClinicalIso);
            var plane = new ArrayPlane(Orientation, ArrayCentre);
            var rows = Rows;
            var settings = _settings;
            var best = _bestScores;
            var built = await Task.Run(() =>
            {
                var targets = rows.Select(r => r.ToTarget()).ToList();
                var model = new PlanningModel(targets, dose);
                var reference = best != null && best.Length == targets.Count ? new CoverageReference(best, new bool[targets.Count]) : null;
                var eval = model.Evaluate(plane, settings, reference);
                var scene = PlaneRenderBuilder.BuildScene(plane.Orientation, plane.Depth, plane.U, plane.V, dose,
                    rows.Select(r => (r.ToTarget(), r.Colour)).ToList(), calculated: true,
                    $"{PlanId} · {plane.Orientation.Label()} · calculated QA dose on the array plane");
                return (eval, scene);
            });
            Metrics.Clear();
            foreach (var m in built.eval.Targets.Where(t => t.AreaInSquare > 0 || AssignedRows.Any(a => a.Id == t.TargetId)))
                Metrics.Add(new MetricRowViewModel(m, rows[m.TargetIndex].Brush));
            Scene = built.scene;
            UpdateOverlay();
        }

        private void UpdateOverlay()
        {
            if (Scene == null) { Overlay = null; return; }
            Overlay = PlaneRenderBuilder.BuildOverlay(Scene, Orientation.U(ArrayCentre), Orientation.V(ArrayCentre), _settings.UsableHalfWidth, ShowDiodes);
        }

        public FineTuneProposal Propose()
        {
            var targets = AssignedRows.Select(r => r.ToTarget()).ToList();
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

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResultText), nameof(IsEditing))] private FineTuneDecision _decision;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResultText))] private string _customX;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResultText))] private string _customY;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResultText))] private string _customZ;

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
                if (iso == null) return "Keep current position.";
                var move = IsoGeometry.ArrayCentreInPatient(Plan.Phantom.UserOrigin, Plan.ClinicalIso, iso.Value) - Plan.ArrayCentre;
                return $"Array: {ShiftLabels.Describe(move)}\nIso rel. user origin: {iso.Value - Plan.Phantom.UserOrigin}";
            }
        }

        private static bool TryParse(string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>Screen 5: calculated QA plans, fine-tuning, deletion.</summary>
    public sealed partial class ReviewViewModel : PageViewModel
    {
        public ReviewViewModel(ShellViewModel shell) : base(shell, "Review & fine-tune", SymbolRegular.CheckmarkCircle24) { }

        public ObservableCollection<QaPlanViewModel> QaPlans => Session.QaPlans;
        public ObservableCollection<FineTuneRowViewModel> Proposals { get; } = new ObservableCollection<FineTuneRowViewModel>();

        [ObservableProperty] private QaPlanViewModel? _selectedQaPlan;
        [ObservableProperty] private string _proposalStatus = "";

        public bool HasProposals => Proposals.Count > 0;

        public override void OnNavigatedTo()
        {
            if (SelectedQaPlan == null || !QaPlans.Contains(SelectedQaPlan)) SelectedQaPlan = QaPlans.FirstOrDefault();
        }

        [RelayCommand]
        private async Task OptimiseFurtherAsync()
        {
            var plans = QaPlans.Where(p => p.CalcOk).ToList();
            if (plans.Count == 0) return;
            ProposalStatus = "Searching for better positions...";
            var proposals = await Shell.ComputeAsync("Fine-tune", () => plans.Select(p => (p, prop: p.Propose())).ToList());
            if (proposals == null) { ProposalStatus = ""; return; }
            Proposals.Clear();
            foreach (var (p, prop) in proposals) Proposals.Add(new FineTuneRowViewModel(p, prop));
            ProposalStatus = "Predictions translate the calculated dose (approximation). Choose Accept, Edit or Discard per plan, then Apply.";
            OnPropertyChanged(nameof(HasProposals));
        }

        [RelayCommand]
        private async Task ApplyAsync()
        {
            var moves = new List<(FineTuneRowViewModel Row, Vec3 Iso)>();
            foreach (var row in Proposals)
            {
                var iso = row.NewIsocentre(out var error);
                if (error != null) { Session.Dialogs.ShowInfo("Check the custom shift", error); return; }
                if (iso != null && (iso.Value - row.Plan.Isocentre).Length > 1e-6) moves.Add((row, iso.Value));
            }
            if (moves.Count == 0) { Session.Dialogs.ShowInfo("Nothing to apply", "All rows are set to Discard or have no shift."); return; }
            if (!Session.Dialogs.Confirm("Move isocentres", $"Move the isocentre and recalculate {moves.Count} QA plan(s)?", "Move and recalculate")) return;

            await Shell.RunAsync($"Moving isocentre and recalculating {moves.Count} plan(s)", async (pr, ct) =>
            {
                foreach (var (row, iso) in moves)
                {
                    ct.ThrowIfCancellationRequested();
                    var plan = row.Plan;
                    var step = pr.BeginStep($"{plan.PlanId}: move isocentre, recalculate");
                    var region = QaPlanRequests.DoseRegion(plan.Orientation, plan.Phantom.UserOrigin);
                    var result = await Session.Service.MoveIsocentreAsync(plan.CourseId, plan.PlanId, iso, region, pr.Reporter, ct);
                    step.State = result.CalculationSucceeded ? "Done" : "Failed";
                    if (!result.CalculationSucceeded) pr.HasErrors = true;
                    plan.Result = result;
                    plan.Iterations++;
                    await plan.RefreshAsync();
                    Proposals.Remove(row);
                }
            });
            OnPropertyChanged(nameof(HasProposals));
            ProposalStatus = Proposals.Count == 0 ? "Applied. Click Optimise further to check again." : ProposalStatus;
        }

        [RelayCommand]
        private Task DeleteSelectedAsync() => DeleteAsync(QaPlans.Where(p => p.IsChecked).ToList());

        [RelayCommand]
        private Task DeleteAllAsync() => DeleteAsync(QaPlans.ToList());

        private async Task DeleteAsync(List<QaPlanViewModel> plans)
        {
            if (plans.Count == 0) { Session.Dialogs.ShowInfo("Delete QA plans", "Tick the plans to delete first."); return; }
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
                    foreach (var p in g)
                    {
                        Session.QaPlans.Remove(p);
                        foreach (var row in Proposals.Where(r => r.Plan == p).ToList()) Proposals.Remove(row);
                    }
                }
            });
            OnPropertyChanged(nameof(HasProposals));
            SelectedQaPlan = QaPlans.FirstOrDefault();
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
