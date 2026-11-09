using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.App.ViewModels
{
    /// <summary>One step of a QA plan in the progress panel (e.g. "Copy beams", "Calculate dose").</summary>
    public sealed partial class PlanStepViewModel : ObservableObject
    {
        public PlanStepViewModel(string name) { Name = name; Started = DateTime.Now; }

        public string Name { get; }
        public DateTime Started { get; private set; }

        /// <summary>Running, Done, Failed.</summary>
        [ObservableProperty] private string _state = "Running";
        [ObservableProperty] private string? _detail;
        [ObservableProperty] private string _durationText = "";

        // Calculation only
        [ObservableProperty] private bool _isCalculation;
        [ObservableProperty] private bool _hasEstimate;
        [ObservableProperty] private double _fraction;
        [ObservableProperty] private string _timingText = "";
        [ObservableProperty] private bool _isOverdue;
        /// <summary>Progress Eclipse reported in its trace output (0-100), when it does.</summary>
        [ObservableProperty] private double? _eclipsePercent;
        /// <summary>Latest Eclipse message during the calculation.</summary>
        [ObservableProperty] private string? _eclipseText;

        public string? Model { get; set; }
        public int ControlPoints { get; set; }
        public TimeSpan? Estimate { get; set; }
        /// <summary>Duration reported by the service when the step ended.</summary>
        public TimeSpan? FinalDuration { get; set; }

        public bool IsRunning => State == "Running";
        partial void OnStateChanged(string value) => OnPropertyChanged(nameof(IsRunning));

        public TimeSpan Elapsed => DateTime.Now - Started;

        public TimeSpan? Remaining
        {
            get
            {
                if (!IsRunning) return null;
                // Eclipse's own percentage, extrapolated linearly, once it's far enough along to mean something.
                if (EclipsePercent is double p && p >= 5) return TimeSpan.FromSeconds(Elapsed.TotalSeconds * (100 - p) / p);
                if (Estimate is TimeSpan e) return e - Elapsed > TimeSpan.Zero ? e - Elapsed : TimeSpan.Zero;
                return null;
            }
        }

        public void Restart() { Started = DateTime.Now; }

        /// <summary>Refresh the elapsed/estimate display of a running step (called every second).</summary>
        public void Tick()
        {
            if (!IsRunning) return;
            var elapsed = Elapsed;
            if (!IsCalculation)
            {
                DurationText = elapsed.TotalSeconds >= 2 ? Format(elapsed) : "";
                return;
            }
            if (EclipsePercent is double pct)
            {
                Fraction = pct / 100.0;
                HasEstimate = true;
                IsOverdue = false;
                TimingText = $"Eclipse: {pct:0}% · {Format(elapsed)} elapsed" +
                             (Remaining is TimeSpan left && pct >= 5 && pct < 100 ? $" · about {FormatRemaining(left)} left" : "");
            }
            else if (Estimate is TimeSpan est && est > TimeSpan.Zero)
            {
                double f = elapsed.TotalSeconds / est.TotalSeconds;
                // Hold short of 100 % until Eclipse says it's finished: this is an estimate, not Eclipse's progress.
                Fraction = Math.Min(f, 0.97);
                IsOverdue = f > 1.25;
                TimingText = IsOverdue
                    ? $"{Format(elapsed)} elapsed; taking longer than usual (estimated {Format(est)}). Eclipse is still calculating."
                    : f >= 1 ? $"{Format(elapsed)} elapsed; should finish shortly"
                    : $"{Format(elapsed)} elapsed · about {FormatRemaining(est - elapsed)} left (est.)";
            }
            else TimingText = $"{Format(elapsed)} elapsed · no estimate yet (first calculation with this model on this computer)";
        }

        public static string Format(TimeSpan t) =>
            t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

        public static string FormatRemaining(TimeSpan t) =>
            t.TotalSeconds < 60 ? "< 1 min" : t.TotalMinutes < 10 ? $"{t.TotalMinutes:0.0} min" : $"{t.TotalMinutes:0} min";
    }

    /// <summary>A QA plan (or "Phantom images") in the progress panel, with its steps.</summary>
    public sealed partial class PlanProgressViewModel : ObservableObject
    {
        public PlanProgressViewModel(string planId, string label, int number)
        {
            PlanId = planId; Label = label; Number = number;
        }

        public string PlanId { get; }
        /// <summary>e.g. "0°, 3 PTVs"; empty for the phantom images group.</summary>
        public string Label { get; }
        /// <summary>1-based plan number; 0 for the phantom images group.</summary>
        public int Number { get; }

        public string Header => Number > 0 ? $"Plan {Number}: {PlanId}" + (Label.Length > 0 ? $"  ·  {Label}" : "") : PlanId;

        /// <summary>Pending, Running, Done, Failed.</summary>
        [ObservableProperty] private string _state = "Pending";
        [ObservableProperty] private string _summary = "";
        [ObservableProperty] private bool _isExpanded;

        public DateTime? Started { get; set; }
        public TimeSpan? Duration { get; set; }

        public ObservableCollection<PlanStepViewModel> Steps { get; } = new ObservableCollection<PlanStepViewModel>();

        public PlanStepViewModel? RunningCalculation => Steps.LastOrDefault(s => s.IsCalculation && s.IsRunning);

        /// <summary>Time of the non-calculation steps so far (to estimate the overhead per plan).</summary>
        public TimeSpan Overhead => Steps.Where(s => !s.IsCalculation && !s.IsRunning)
            .Aggregate(TimeSpan.Zero, (t, s) => t + (s.FinalDuration ?? TimeSpan.Zero));
    }

    /// <summary>Row of the MU check table.</summary>
    public sealed class MuRowViewModel
    {
        public MuRowViewModel(BeamMuResult r) { Result = r; }
        public BeamMuResult Result { get; }
        public string BeamId => Result.BeamId;
        public string QaMu => double.IsNaN(Result.QaMu) ? "-" : Result.QaMu.ToString("0.00");
        public string ClinicalMu => Result.ClinicalMu.ToString("0.00");
        public string Difference => double.IsNaN(Result.QaMu) ? "-" : (Result.QaMu - Result.ClinicalMu).ToString("+0.000;-0.000;0.000");
        public bool Matches => Result.Matches;
    }
}
