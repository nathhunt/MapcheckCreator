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
    public sealed class MetricRowViewModel
    {
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
        public PlanePlanViewModel(int number, PlaneEvaluation evaluation, IReadOnlyList<PtvRowViewModel> rows)
        {
            Number = number;
            Rows = rows;
            Update(evaluation);
        }

        public int Number { get; private set; }
        public IReadOnlyList<PtvRowViewModel> Rows { get; }
        public PlaneEvaluation Evaluation { get; private set; } = null!;
        public ArrayOrientation Orientation => Evaluation.Plane.Orientation;
        public Vec3 Centre => Evaluation.Plane.Centre;
        public ObservableCollection<MetricRowViewModel> Metrics { get; } = new ObservableCollection<MetricRowViewModel>();

        [ObservableProperty] private bool _isManual;
        [ObservableProperty] private string _centreX = "";
        [ObservableProperty] private string _centreY = "";
        [ObservableProperty] private string _centreZ = "";

        public string Title => $"Plan {Number} · {Orientation.Label()}";
        public string OrientationText => Orientation.Label();

        /// <summary>Depth along the array normal, with patient direction.</summary>
        public string DepthText => Orientation == ArrayOrientation.Deg0
            ? string.Format(CultureInfo.InvariantCulture, "AP: y = {0:0.0} mm", Centre.Y)
            : string.Format(CultureInfo.InvariantCulture, "LR: x = {0:0.0} mm", Centre.X);

        public string CentreText => Centre.ToString();

        // Nudge row labels: what − and + mean in patient directions for this orientation (array relative to patient).
        public string UText => Orientation == ArrayOrientation.Deg0 ? "In-plane x:  − Right / + Left" : "In-plane y:  − Ant / + Post";
        public string VText => "In-plane z:  − Inf / + Sup";
        public string NText => Orientation == ArrayOrientation.Deg0 ? "Depth y:  − Ant / + Post" : "Depth x:  − Right / + Left";
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

        public void SetNumber(int n) { Number = n; OnPropertyChanged(nameof(Number)); OnPropertyChanged(nameof(Title)); }

        public void Update(PlaneEvaluation evaluation)
        {
            Evaluation = evaluation;
            Metrics.Clear();
            foreach (var m in evaluation.Targets) Metrics.Add(new MetricRowViewModel(m, Rows[m.TargetIndex].Brush));
            CentreX = Centre.X.ToString("0.0", CultureInfo.InvariantCulture);
            CentreY = Centre.Y.ToString("0.0", CultureInfo.InvariantCulture);
            CentreZ = Centre.Z.ToString("0.0", CultureInfo.InvariantCulture);
            foreach (var p in new[] { nameof(Evaluation), nameof(Orientation), nameof(Centre), nameof(Title), nameof(OrientationText),
                                      nameof(DepthText), nameof(CentreText), nameof(CoveredText), nameof(ScoreText),
                                      nameof(UText), nameof(VText), nameof(NText) })
                OnPropertyChanged(p);
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
