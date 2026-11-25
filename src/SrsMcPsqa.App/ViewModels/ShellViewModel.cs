using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Threading;
using SrsMcPsqa.App.Infrastructure;
using SrsMcPsqa.Core.Services;
using Wpf.Ui.Controls;

namespace SrsMcPsqa.App.ViewModels
{
    /// <summary>Base for the screens shown in the left navigation.</summary>
    public abstract partial class PageViewModel : ObservableObject
    {
        protected PageViewModel(ShellViewModel shell, string title, SymbolRegular symbol, int step = 0)
        {
            Shell = shell; Title = title; Symbol = symbol; Step = step;
        }

        /// <summary>Workflow step number (1-4); 0 for Settings.</summary>
        public int Step { get; }

        public ShellViewModel Shell { get; }
        public PlanningSession Session => Shell.Session;
        public string Title { get; }
        public SymbolRegular Symbol { get; }

        [ObservableProperty] private bool _isAvailable = true;

        /// <summary>Step completed (shown as a tick in the navigation).</summary>
        [ObservableProperty] private bool _isDone;

        public virtual void OnNavigatedTo() { }
    }

    /// <summary>One step in the progress panel.</summary>
    public sealed partial class ProgressStepViewModel : ObservableObject
    {
        public ProgressStepViewModel(string text) { Text = text; }
        public string Text { get; }
        /// <summary>Pending, Running, Done, Failed.</summary>
        [ObservableProperty] private string _state = "Running";
    }

    /// <summary>
    /// Progress panel for long operations. Generic operations show a step list and log. QA plan operations also show
    /// one card per plan with its steps and durations, an estimated calculation progress (Eclipse doesn't report
    /// progress inside a calculation, so the estimate comes from earlier calculations), an overall estimate and the MU check.
    /// </summary>
    public sealed partial class ProgressViewModel : ObservableObject
    {
        private CancellationTokenSource? _cts;
        private readonly DispatcherTimer _timer;
        private DateTime _started;
        private readonly CalcTimingStore _timings;

        public ProgressViewModel(CalcTimingStore? timings = null)
        {
            _timings = timings ?? CalcTimingStore.Shared;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, __) => Tick();
        }

        [ObservableProperty] private bool _isOpen;
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private string _title = "";
        [ObservableProperty] private string _currentText = "";
        [ObservableProperty] private bool _cancelRequested;
        [ObservableProperty] private bool _hasErrors;

        // Overall (QA plan operations)
        [ObservableProperty] private string _elapsedText = "";
        [ObservableProperty] private string _overallText = "";
        [ObservableProperty] private string _overallRemainingText = "";
        [ObservableProperty] private double _overallFraction;
        [ObservableProperty] private bool _overallHasEstimate;
        [ObservableProperty] private bool _hasPlans;
        [ObservableProperty] private string _muTitle = "";
        [ObservableProperty] private bool _muAllMatch;
        [ObservableProperty] private bool _hasMu;

        public ObservableCollection<ProgressStepViewModel> Steps { get; } = new ObservableCollection<ProgressStepViewModel>();
        public ObservableCollection<string> LogLines { get; } = new ObservableCollection<string>();
        public ObservableCollection<PlanProgressViewModel> Plans { get; } = new ObservableCollection<PlanProgressViewModel>();
        public ObservableCollection<MuRowViewModel> MuRows { get; } = new ObservableCollection<MuRowViewModel>();

        /// <summary>Reports from services, marshalled to the UI thread (Progress&lt;T&gt; captures the UI context).</summary>
        public IProgress<string> Reporter { get; private set; } = new Progress<string>();

        /// <summary>Structured per-plan step reports, marshalled to the UI thread.</summary>
        public IProgress<QaProgressEvent> Events { get; private set; } = new Progress<QaProgressEvent>();

        public CancellationToken Start(string title)
        {
            Title = title; Steps.Clear(); LogLines.Clear(); Plans.Clear(); MuRows.Clear(); CurrentText = ""; HasErrors = false;
            ElapsedText = ""; OverallText = ""; OverallRemainingText = ""; OverallFraction = 0; OverallHasEstimate = false;
            HasPlans = false; MuTitle = ""; MuAllMatch = false; HasMu = false;
            CancelRequested = false; IsRunning = true; IsOpen = true;
            Reporter = new Progress<string>(AddLog);
            Events = new Progress<QaProgressEvent>(OnPlanEvent);
            _cts = new CancellationTokenSource();
            _started = DateTime.Now;
            _timer.Start();
            return _cts.Token;
        }

        /// <summary>Lists the QA plans about to be processed, so the panel can show "Plan k of N" and estimate the total.</summary>
        public void ExpectPlans(IEnumerable<(string PlanId, string Label)> plans)
        {
            int n = 0;
            foreach (var (id, label) in plans) Plans.Add(new PlanProgressViewModel(id, label, ++n));
            HasPlans = Plans.Count > 0;
            UpdateOverall();
        }

        public void AddLog(string line)
        {
            CurrentText = line;
            LogLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
            Log.Info(line);
        }

        public ProgressStepViewModel BeginStep(string text)
        {
            var s = new ProgressStepViewModel(text);
            Steps.Add(s);
            AddLog(text);
            return s;
        }

        public void OnPlanEvent(QaProgressEvent e)
        {
            var plan = Plans.FirstOrDefault(p => string.Equals(p.PlanId, e.PlanId, StringComparison.OrdinalIgnoreCase));
            if (plan == null)
            {
                // Not announced (e.g. "Phantom images"): show it before the numbered plans.
                plan = new PlanProgressViewModel(e.PlanId, "", 0);
                Plans.Insert(Plans.Count(p => p.Number == 0), plan);
                HasPlans = true;
            }
            if (plan.State == "Pending")
            {
                plan.State = "Running"; plan.Started = DateTime.Now; plan.IsExpanded = true;
                // A finished group before this plan (e.g. "Phantom images") is done once the next plan starts.
                foreach (var g in Plans.Where(p => p.Number == 0 && p.State == "Running" && p != plan && p.Steps.All(s => s.State == "Done")).ToList())
                {
                    g.State = "Done";
                    g.IsExpanded = false;
                    g.Summary = $"{g.Steps.Count} step{(g.Steps.Count == 1 ? "" : "s")} done";
                }
                foreach (var other in Plans.Where(p => p != plan && p.State != "Running")) other.IsExpanded = false;
            }

            var step = plan.Steps.LastOrDefault(s => s.IsRunning && s.Name == e.Step);
            if (step == null)
            {
                step = new PlanStepViewModel(e.Step);
                plan.Steps.Add(step);
            }
            if (e.Detail != null) step.Detail = e.Detail;

            if (e.IsCalculation && e.State == QaStepState.Running && (e.EclipsePercent.HasValue || e.EclipseMessage != null))
            {
                // Live update from Eclipse's trace output during the calculation.
                if (e.EclipsePercent.HasValue) step.EclipsePercent = e.EclipsePercent;
                if (e.EclipseMessage != null) step.EclipseText = e.EclipseMessage;
                step.Tick();
                UpdateOverall();
                return;
            }

            if (e.IsCalculation)
            {
                step.IsCalculation = true;
                if (e.Model != null) step.Model = e.Model;
                if (e.ControlPoints > 0) step.ControlPoints = e.ControlPoints;
                if (e.State == QaStepState.Running)
                {
                    step.Restart();
                    step.Estimate = _timings.Estimate(step.Model, step.ControlPoints);
                    step.HasEstimate = step.Estimate.HasValue;
                    step.Fraction = 0;
                    step.EclipsePercent = null;
                    step.EclipseText = null;
                }
            }

            switch (e.State)
            {
                case QaStepState.Running:
                    step.State = "Running";
                    Log.Info($"{e.PlanId}: {e.Step}...");
                    step.Tick();
                    break;
                case QaStepState.Done:
                case QaStepState.Failed:
                    step.State = e.State == QaStepState.Done ? "Done" : "Failed";
                    step.FinalDuration = e.Duration ?? step.Elapsed;
                    step.DurationText = step.FinalDuration.Value.TotalSeconds >= 1 || step.IsCalculation ? PlanStepViewModel.Format(step.FinalDuration.Value) : "";
                    if (step.IsCalculation)
                    {
                        step.Fraction = 1;
                        step.IsOverdue = false;
                        step.TimingText = "";
                        step.EclipseText = null;
                        if (e.State == QaStepState.Done) _timings.Record(step.Model, step.ControlPoints, step.FinalDuration.Value);
                    }
                    if (e.Warnings != null && e.Warnings.Count > 0) step.WarningsText = string.Join(Environment.NewLine, e.Warnings);
                    if (e.State == QaStepState.Failed) { plan.State = "Failed"; HasErrors = true; }
                    Log.Info($"{e.PlanId}: {e.Step} {step.State.ToLowerInvariant()} in {step.FinalDuration.Value.TotalSeconds:0.0} s{(e.Detail != null ? " (" + e.Detail + ")" : "")}.");
                    break;
            }

            if (e.MuResults != null && e.MuResults.Count > 0)
            {
                MuRows.Clear();
                foreach (var r in e.MuResults) MuRows.Add(new MuRowViewModel(r));
                MuAllMatch = e.MuResults.All(r => r.Matches);
                MuTitle = $"MU check, {e.PlanId}";
                HasMu = true;
            }
            UpdateOverall();
        }

        /// <summary>Mark a plan finished (called by the caller once the service call returns or throws).</summary>
        public void CompletePlan(string planId, bool success, string? summary = null)
        {
            var plan = Plans.FirstOrDefault(p => string.Equals(p.PlanId, planId, StringComparison.OrdinalIgnoreCase));
            if (plan == null) return;
            foreach (var s in plan.Steps.Where(s => s.IsRunning)) { s.State = success ? "Done" : "Failed"; s.TimingText = ""; }
            plan.State = success && plan.State != "Failed" ? "Done" : "Failed";
            if (!success) HasErrors = true;
            if (plan.Started is DateTime t) plan.Duration = DateTime.Now - t;
            plan.Summary = summary ?? (plan.Duration is TimeSpan d ? $"{plan.State.ToLowerInvariant()} in {PlanStepViewModel.Format(d)}" : plan.State);
            plan.IsExpanded = !success;
            UpdateOverall();
        }

        private void Tick()
        {
            if (!IsRunning) return;
            ElapsedText = PlanStepViewModel.Format(DateTime.Now - _started);
            foreach (var p in Plans.Where(p => p.State == "Running"))
                foreach (var s in p.Steps.Where(s => s.IsRunning)) s.Tick();
            UpdateOverall();
        }

        private void UpdateOverall()
        {
            var numbered = Plans.Where(p => p.Number > 0).ToList();
            if (numbered.Count == 0) { OverallText = ""; OverallRemainingText = ""; OverallHasEstimate = false; return; }

            int finished = numbered.Count(p => p.State == "Done" || p.State == "Failed");
            var current = numbered.FirstOrDefault(p => p.State == "Running");
            int k = current?.Number ?? Math.Min(finished + 1, numbered.Count);
            OverallText = finished == numbered.Count ? $"{numbered.Count} of {numbered.Count} plans finished" : $"Plan {k} of {numbered.Count}";

            // Typical plan: its calculation estimate plus the measured time of the other steps of finished plans.
            var calcEstimate = numbered.SelectMany(p => p.Steps).Where(s => s.IsCalculation && s.Estimate.HasValue).Select(s => s.Estimate!.Value)
                .DefaultIfEmpty(TimeSpan.Zero).Last();
            var lastCalc = numbered.SelectMany(p => p.Steps).LastOrDefault(s => s.IsCalculation && s.FinalDuration.HasValue);
            var typicalCalc = calcEstimate > TimeSpan.Zero ? calcEstimate : lastCalc?.FinalDuration ?? TimeSpan.Zero;
            // Prefer the running calculation's own projection from Eclipse's percentage (same beams on every plan).
            var running = current?.RunningCalculation;
            if (running?.EclipsePercent is double pct && pct >= 5)
                typicalCalc = TimeSpan.FromSeconds(running.Elapsed.TotalSeconds * 100 / pct);
            var done = numbered.Where(p => p.State == "Done").ToList();
            var overhead = done.Count > 0 ? TimeSpan.FromSeconds(done.Average(p => p.Overhead.TotalSeconds)) : TimeSpan.FromSeconds(20);

            if (typicalCalc <= TimeSpan.Zero || !IsRunning)
            {
                OverallHasEstimate = false;
                OverallFraction = (double)finished / numbered.Count;
                OverallRemainingText = IsRunning ? "Overall estimate after the first calculation" : "";
                return;
            }

            var perPlan = typicalCalc + overhead;
            int pending = numbered.Count(p => p.State == "Pending");
            TimeSpan currentLeft = TimeSpan.Zero;
            double currentFraction = 0;
            if (current != null)
            {
                var calc = current.RunningCalculation;
                bool calcDone = current.Steps.Any(s => s.IsCalculation && s.State == "Done");
                if (calc != null)
                {
                    currentLeft = (calc.Remaining ?? typicalCalc - calc.Elapsed) + TimeSpan.FromSeconds(overhead.TotalSeconds * 0.3);
                    currentFraction = calc.HasEstimate ? 0.1 + 0.85 * calc.Fraction : 0.5;
                }
                else if (calcDone) { currentLeft = TimeSpan.FromSeconds(overhead.TotalSeconds * 0.3); currentFraction = 0.95; }
                else { currentLeft = perPlan; currentFraction = 0.05; }
                if (currentLeft < TimeSpan.Zero) currentLeft = TimeSpan.Zero;
            }
            var left = currentLeft + TimeSpan.FromTicks(perPlan.Ticks * pending);
            OverallHasEstimate = true;
            OverallFraction = Math.Min(1, (finished + currentFraction) / numbered.Count);
            OverallRemainingText = $"about {PlanStepViewModel.FormatRemaining(left)} left in total (est.)";
        }

        public void Finish(bool keepOpen)
        {
            IsRunning = false;
            _timer.Stop();
            ElapsedText = PlanStepViewModel.Format(DateTime.Now - _started);
            foreach (var s in Steps.Where(s => s.State == "Running")) s.State = HasErrors ? "Failed" : "Done";
            foreach (var p in Plans.Where(p => p.State == "Running" && p.Number == 0)) p.State = HasErrors ? "Failed" : "Done";
            foreach (var p in Plans.Where(p => p.State == "Running").ToList()) CompletePlan(p.PlanId, success: !HasErrors);
            UpdateOverall();
            if (Plans.Count > 0)
            {
                OverallRemainingText = "";
                if (CancelRequested) OverallText += "; cancelled";
            }
            if (!keepOpen) IsOpen = false;
        }

        [RelayCommand]
        private void Cancel()
        {
            CancelRequested = true;
            AddLog("Cancel requested: stops after the current plan is finished (an Eclipse calculation can't be interrupted).");
            _cts?.Cancel();
        }

        [RelayCommand]
        private void Close() { if (!IsRunning) IsOpen = false; }
    }

    /// <summary>Main window: navigation, the shared session and long-operation handling.</summary>
    public sealed partial class ShellViewModel : ObservableObject
    {
        public ShellViewModel(PlanningSession session, bool isFake)
        {
            Session = session;
            IsFake = isFake;
            PatientPlan = new PatientPlanViewModel(this);
            Ptvs = new PtvsViewModel(this);
            Planning = new PlanningViewModel(this);
            Review = new ReviewViewModel(this);
            Settings = new SettingsViewModel(this);
            Steps = new ObservableCollection<PageViewModel> { PatientPlan, Ptvs, Planning, Review };
            Utilities = new ObservableCollection<PageViewModel> { Settings };
            _selectedPage = PatientPlan;
            _selectedStep = PatientPlan;
            UpdateAvailability();
            Session.PropertyChanged += (_, __) => UpdateAvailability();
            Session.QaPlans.CollectionChanged += (_, __) => UpdateAvailability();
            Planning.Planes.CollectionChanged += (_, __) => UpdateAvailability();
        }

        public PlanningSession Session { get; }
        public bool IsFake { get; }
        public ProgressViewModel Progress { get; } = new ProgressViewModel();

        public PatientPlanViewModel PatientPlan { get; }
        public PtvsViewModel Ptvs { get; }
        public PlanningViewModel Planning { get; }
        public ReviewViewModel Review { get; }
        public SettingsViewModel Settings { get; }
        /// <summary>The numbered workflow steps (left navigation).</summary>
        public ObservableCollection<PageViewModel> Steps { get; }

        /// <summary>Pages outside the workflow (Settings), shown at the bottom of the navigation.</summary>
        public ObservableCollection<PageViewModel> Utilities { get; }

        [ObservableProperty] private PageViewModel _selectedPage;
        [ObservableProperty] private PageViewModel? _selectedStep;
        [ObservableProperty] private PageViewModel? _selectedUtility;

        partial void OnSelectedStepChanged(PageViewModel? value)
        {
            if (value == null) return;
            SelectedUtility = null;
            SelectedPage = value;
        }

        partial void OnSelectedUtilityChanged(PageViewModel? value)
        {
            if (value == null) return;
            SelectedStep = null;
            SelectedPage = value;
        }

        /// <summary>Version with the git commit it was built from, e.g. 1.0.2-beta+a1b2c3d (-dirty: uncommitted changes).</summary>
        public static string Version { get; } = Infrastructure.Diagnostics.AppVersion;
        public string WindowTitle => $"SRS MapCHECK PSQA Plan Creator {Version}" + (IsFake ? " · DEMO DATA" : "");

        partial void OnSelectedPageChanged(PageViewModel value)
        {
            if (value != null) Log.Info($"Screen: {value.Title}");
            value?.OnNavigatedTo();
        }

        public void Navigate(PageViewModel page)
        {
            page.IsAvailable = true;
            if (Steps.Contains(page)) SelectedStep = page; else SelectedUtility = page;
        }

        private void UpdateAvailability()
        {
            bool plan = Session.HasPlan;
            Ptvs.IsAvailable = plan;
            Planning.IsAvailable = plan;
            Review.IsAvailable = Session.QaPlans.Count > 0;
            PatientPlan.IsDone = plan;
            Ptvs.IsDone = plan && Planning.Planes.Count > 0;
            Planning.IsDone = Session.QaPlans.Count > 0;
        }

        /// <summary>
        /// Run a long operation with the progress panel. Exceptions are shown in an error dialog (never swallowed).
        /// Returns true on success. Cancellation takes effect between operations.
        /// </summary>
        public async Task<bool> RunAsync(string title, Func<ProgressViewModel, CancellationToken, Task> body, bool keepOpenOnSuccess = false)
        {
            if (Progress.IsRunning) return false;
            var ct = Progress.Start(title);
            Log.Info($"Start: {title}");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await body(Progress, ct);
                Progress.AddLog("Finished.");
                Log.Info($"Finished: {title} in {sw.Elapsed.TotalSeconds:0.0} s.");
                Progress.Finish(keepOpenOnSuccess);
                return true;
            }
            catch (OperationCanceledException)
            {
                Log.Info($"Cancelled: {title} after {sw.Elapsed.TotalSeconds:0.0} s.");
                Progress.AddLog("Cancelled.");
                Progress.HasErrors = true;
                Progress.Finish(keepOpen: true);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed: {title} after {sw.Elapsed.TotalSeconds:0.0} s.", ex);
                Progress.HasErrors = true;
                Progress.AddLog("Error: " + ex.Message);
                Progress.Finish(keepOpen: true);
                Session.Dialogs.ShowError(title, ex.Message, ex);
                return false;
            }
        }

        /// <summary>Run a short CPU-bound task off the UI thread with error handling (no progress panel).</summary>
        public async Task<T?> ComputeAsync<T>(string what, Func<T> work) where T : class
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var r = await Task.Run(work);
                Log.Info($"Computed: {what} in {sw.Elapsed.TotalSeconds:0.00} s.");
                return r;
            }
            catch (Exception ex)
            {
                Log.Error($"Compute failed: {what} after {sw.Elapsed.TotalSeconds:0.00} s.", ex);
                Session.Dialogs.ShowError(what, ex.Message, ex);
                return null;
            }
        }
    }
}
