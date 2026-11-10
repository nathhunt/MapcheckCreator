using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SrsMcPsqa.App.Infrastructure;
using SrsMcPsqa.Core.Geometry;
using SrsMcPsqa.Core.Models;

namespace SrsMcPsqa.App.ViewModels
{
    /// <summary>A labelled choice for combo boxes.</summary>
    public sealed class Choice<T>
    {
        public Choice(T value, string text) { Value = value; Text = text; }
        public T Value { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    /// <summary>Per-PTV metrics on one plane, formatted for the metrics table.</summary>
    public sealed partial class MetricRowViewModel : ObservableObject
    {
        /// <summary>The QA plan(s) this PTV is assigned to, e.g. "Plan 2 · 270°".</summary>
        [ObservableProperty] private string _planText = "";
        /// <summary>True when the PTV is assigned to the plan whose table this row is in.</summary>
        [ObservableProperty] private bool _isOnThisPlan;

        public MetricRowViewModel(PtvPlaneMetrics m, Brush brush)
        {
            M = m; Brush = brush;
        }

        public PtvPlaneMetrics M { get; }
        public Brush Brush { get; }
        public string Id => M.TargetId;
        public string Rx => M.RxGy.ToString("0.##", CultureInfo.InvariantCulture);
        public string Area => M.AreaInSquare.ToString("0", CultureInfo.InvariantCulture);
        public string MaxArea => M.MaxArea.ToString("0", CultureInfo.InvariantCulture);
        public string HighDose => (100 * M.HighDoseFraction).ToString("0", CultureInfo.InvariantCulture) + " %";
        public string MeanRatio => M.MeanDoseRatio.ToString("0.00", CultureInfo.InvariantCulture);
        public string Score => M.Score.ToString("0.000", CultureInfo.InvariantCulture);
        public string Relative => M.BestScore > 0 ? (100 * M.RelativeScore).ToString("0", CultureInfo.InvariantCulture) + " %" : "–";
        public int Diodes => M.DiodesInPtv;
        public int DiodesAtRx => M.DiodesAtOrAboveRx;
        public bool OnPlane => M.AreaInSquare > 0;
        public string Status => !OnPlane ? "not on plane" : M.Covered ? (M.Clipped ? "◐ covered, clipped" : "✓ covered") : (M.Clipped ? "◐ clipped" : "– below threshold");
    }

    /// <summary>One planned array plane (one future QA plan).</summary>
    public sealed partial class PlanePlanViewModel : ObservableObject
    {
        public PlanePlanViewModel(int number, PlaneEvaluation evaluation, IReadOnlyList<PtvRowViewModel> rows, Vec3 clinicalIso)
        {
            Number = number;
            Rows = rows;
            ClinicalIso = clinicalIso;
            RecommendedCentre = evaluation.Plane.Centre;
            Update(evaluation);
        }

        /// <summary>Position as recommended/re-optimised (for Reset).</summary>
        public Vec3 RecommendedCentre { get; }

        public int Number { get; private set; }
        public Vec3 ClinicalIso { get; }

        /// <summary>
        /// QA isocentre relative to the MapCHECK (phantom) user origin, which is the array centre: clinical isocentre − array centre
        /// (<see cref="IsoGeometry.QaIsocentre"/>). Known before the phantom is copied.
        /// </summary>
        public Vec3 IsoFromOrigin => ClinicalIso - Centre;
        public string IsoFromOriginText => ShiftLabels.Describe(IsoFromOrigin);
        public IReadOnlyList<PtvRowViewModel> Rows { get; }
        public PlaneEvaluation Evaluation { get; private set; } = null!;
        public ArrayOrientation Orientation => Evaluation.Plane.Orientation;
        public Vec3 Centre => Evaluation.Plane.Centre;
        public ObservableCollection<MetricRowViewModel> Metrics { get; } = new ObservableCollection<MetricRowViewModel>();

        [ObservableProperty] private bool _isManual;
        // Editable QA isocentre relative to the MapCHECK user origin (mm; +x Left, +y Post, +z Sup).
        [ObservableProperty] private string _isoX = "";
        [ObservableProperty] private string _isoY = "";
        [ObservableProperty] private string _isoZ = "";

        public string Title => $"Plan {Number} · {Orientation.Label()}";
        public string OrientationText => Orientation.Label();

        /// <summary>Depth along the array normal, with patient direction.</summary>
        public string DepthText => Orientation == ArrayOrientation.Deg0
            ? string.Format(CultureInfo.InvariantCulture, "AP: y = {0:0.0} mm", Centre.Y)
            : string.Format(CultureInfo.InvariantCulture, "LR: x = {0:0.0} mm", Centre.X);

        public string CentreText => Centre.ToString();

        // Move-button labels: patient directions for this orientation (array moves relative to the patient).
        public string UMinusText => Orientation == ArrayOrientation.Deg0 ? "R" : "A";
        public string UPlusText => Orientation == ArrayOrientation.Deg0 ? "L" : "P";
        public string NMinusText => Orientation == ArrayOrientation.Deg0 ? "Ant" : "Right";
        public string NPlusText => Orientation == ArrayOrientation.Deg0 ? "Post" : "Left";
        public string DepthAxisText => Orientation == ArrayOrientation.Deg0 ? "Depth (anterior–posterior)" : "Depth (right–left)";

        /// <summary>PTVs this plan is for.</summary>
        public string PtvText => string.Join(", ", AssignedIndices.Select(k => Rows[k].Id));

        /// <summary>One-line status of this plan's PTVs.</summary>
        public string StatusText
        {
            get
            {
                var m = AssignedIndices.Select(k => Evaluation.Targets[k]).ToList();
                if (m.Count == 0) return "No PTV on this plane";
                if (m.Any(t => !t.Covered)) return "✗ not all PTVs covered";
                if (m.Any(t => t.Clipped)) return "◐ PTV extends beyond the array";
                return "✓ PTVs covered";
            }
        }

        public System.Windows.Media.Brush StatusBrush => StatusText.StartsWith("✓") ? StatusBrushConverter.Good
            : StatusText.StartsWith("◐") ? StatusBrushConverter.Caution : StatusBrushConverter.Bad;
        public string CoveredText => string.Join(", ", Evaluation.Targets.Where(t => t.Covered).Select(t => t.TargetId));
        public string ScoreText => Evaluation.Targets.Where(t => t.Covered).Sum(t => t.Score).ToString("0.00", CultureInfo.InvariantCulture);

        public IReadOnlyList<int> AssignedIndices
        {
            get
            {
                var covered = Evaluation.Targets.Where(t => t.Covered).Select(t => t.TargetIndex).ToList();
                return covered.Count > 0 ? covered : Evaluation.Targets.Where(t => t.AreaInSquare > 0).Select(t => t.TargetIndex).ToList();
            }
        }

        public void SetNumber(int n) { Number = n; OnPropertyChanged(nameof(Number)); OnPropertyChanged(nameof(Title)); Updated?.Invoke(this, System.EventArgs.Empty); }

        /// <summary>Raised after the evaluation (and so the PTV assignment) changes.</summary>
        public event System.EventHandler? Updated;

        public void Update(PlaneEvaluation evaluation)
        {
            Evaluation = evaluation;
            Metrics.Clear();
            foreach (var m in evaluation.Targets) Metrics.Add(new MetricRowViewModel(m, Rows[m.TargetIndex].Brush));
            IsoX = IsoFromOrigin.X.ToString("0.0", CultureInfo.InvariantCulture);
            IsoY = IsoFromOrigin.Y.ToString("0.0", CultureInfo.InvariantCulture);
            IsoZ = IsoFromOrigin.Z.ToString("0.0", CultureInfo.InvariantCulture);
            foreach (var p in new[] { nameof(Evaluation), nameof(Orientation), nameof(Centre), nameof(Title), nameof(OrientationText),
                                      nameof(DepthText), nameof(CentreText), nameof(CoveredText), nameof(ScoreText),
                                      nameof(UMinusText), nameof(UPlusText), nameof(NMinusText), nameof(NPlusText), nameof(DepthAxisText),
                                      nameof(PtvText), nameof(StatusText), nameof(StatusBrush), nameof(IsoFromOrigin), nameof(IsoFromOriginText) })
                OnPropertyChanged(p);
            Updated?.Invoke(this, System.EventArgs.Empty);
        }
    }

    /// <summary>Coverage summary line for one PTV.</summary>
    public sealed class CoverageRowViewModel
    {
        public CoverageRowViewModel(string id, Brush brush, CoverageStatus status, string planeText)
        {
            Id = id; Brush = brush; Status = status; PlaneText = planeText;
        }

        public string Id { get; }
        public Brush Brush { get; }
        public CoverageStatus Status { get; }
        public string StatusText => CoverageTextConverter.Text(Status);
        public string PlaneText { get; }
    }

    /// <summary>One cell of the PTV × plan assignment matrix.</summary>
    public sealed partial class AssignmentCellViewModel : ObservableObject
    {
        public AssignmentCellViewModel(int targetIndex, string id) { TargetIndex = targetIndex; Id = id; }
        public int TargetIndex { get; }
        public string Id { get; }
        [ObservableProperty] private bool _isAssigned;
    }

    /// <summary>One row of the constraint editor: a plan's orientation and its required PTVs.</summary>
    public sealed partial class ConstraintRowViewModel : ObservableObject
    {
        public ConstraintRowViewModel(int number, IEnumerable<AssignmentCellViewModel> cells)
        {
            Number = number;
            foreach (var c in cells) Cells.Add(c);
            _orientation = OrientationChoices[0];
        }

        public static IReadOnlyList<Choice<ArrayOrientation?>> OrientationChoices { get; } = new[]
        {
            new Choice<ArrayOrientation?>(null, "Auto"),
            new Choice<ArrayOrientation?>(ArrayOrientation.Deg0, "0°"),
            new Choice<ArrayOrientation?>(ArrayOrientation.Deg270, "270°"),
        };

        public int Number { get; set; }
        public string Label => $"Plan {Number}";
        public ObservableCollection<AssignmentCellViewModel> Cells { get; } = new ObservableCollection<AssignmentCellViewModel>();

        [ObservableProperty] private Choice<ArrayOrientation?> _orientation;
    }
}
