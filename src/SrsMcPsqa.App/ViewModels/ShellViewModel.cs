using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SrsMcPsqa.App.Infrastructure;
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

    /// <summary>Inline progress panel for long operations (copying images, creating plans, calculating).</summary>
    public sealed partial class ProgressViewModel : ObservableObject
    {
        private CancellationTokenSource? _cts;

        [ObservableProperty] private bool _isOpen;
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private string _title = "";
        [ObservableProperty] private string _currentText = "";
        [ObservableProperty] private bool _cancelRequested;
        [ObservableProperty] private bool _hasErrors;

        public ObservableCollection<ProgressStepViewModel> Steps { get; } = new ObservableCollection<ProgressStepViewModel>();
        public ObservableCollection<string> LogLines { get; } = new ObservableCollection<string>();

        /// <summary>Reports from services, marshalled to the UI thread (Progress&lt;T&gt; captures the UI context).</summary>
        public IProgress<string> Reporter { get; private set; } = new Progress<string>();

        public CancellationToken Start(string title)
        {
            Title = title; Steps.Clear(); LogLines.Clear(); CurrentText = ""; HasErrors = false;
            CancelRequested = false; IsRunning = true; IsOpen = true;
            Reporter = new Progress<string>(AddLog);
            _cts = new CancellationTokenSource();
            return _cts.Token;
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

        public void Finish(bool keepOpen)
        {
            IsRunning = false;
            foreach (var s in Steps.Where(s => s.State == "Running")) s.State = HasErrors ? "Failed" : "Done";
            if (!keepOpen) IsOpen = false;
        }

        [RelayCommand]
        private void Cancel()
        {
            CancelRequested = true;
            AddLog("Cancel requested: stops after the current step (an Eclipse calculation can't be interrupted).");
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

        public string WindowTitle => "SRS MapCHECK PSQA Plan Creator" + (IsFake ? " · DEMO DATA" : "");

        partial void OnSelectedPageChanged(PageViewModel value) => value?.OnNavigatedTo();

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
            try
            {
                await body(Progress, ct);
                Progress.AddLog("Finished.");
                Progress.Finish(keepOpenOnSuccess);
                return true;
            }
            catch (OperationCanceledException)
            {
                Progress.AddLog("Cancelled.");
                Progress.HasErrors = true;
                Progress.Finish(keepOpen: true);
                return false;
            }
            catch (Exception ex)
            {
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
            try
            {
                return await Task.Run(work);
            }
            catch (Exception ex)
            {
                Session.Dialogs.ShowError(what, ex.Message, ex);
                return null;
            }
        }
    }
}
