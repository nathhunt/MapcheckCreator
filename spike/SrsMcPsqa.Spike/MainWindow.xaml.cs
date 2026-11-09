using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using EsapiApplication = VMS.TPS.Common.Model.API.Application;
// WPF-UI (Wpf.Ui.Controls) defines its own MessageBox types; use the WPF ones.
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace SrsMcPsqa.Spike
{
    /// <summary>
    /// Spike window. Code-behind is acceptable here (throwaway spike); the real app uses MVVM.
    /// Every ESAPI call goes through <see cref="EsapiWorker"/>.
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        private readonly string _reportFolder;
        private readonly string _reportPath;
        private readonly object _fileLock = new object();
        private readonly SpikeRunner _runner;
        private EsapiWorker? _worker;

        internal MainWindow(LaunchArgs launch)
        {
            InitializeComponent();

            _reportFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SrsMcPsqa", "spike");
            Directory.CreateDirectory(_reportFolder);
            _reportPath = Path.Combine(_reportFolder, $"spike-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            _runner = new SpikeRunner(Log);

            var saved = SpikeInputs.LoadOrDefault();
            PatientIdBox.Text = launch.PatientId;
            ClinicalCourseBox.Text = launch.CourseId;
            ClinicalPlanBox.Text = launch.PlanId;
            TemplatePatientBox.Text = saved.TemplatePatientId;
            TemplateStudyBox.Text = saved.TemplateStudyId;
            TemplateImageBox.Text = saved.TemplateImageId;
            Iso1Box.Text = SpikeInputs.FormatVector(saved.IsoOffset1);
            Iso2Box.Text = SpikeInputs.FormatVector(saved.IsoOffset2);

            Log("SRS MapCHECK PSQA - Phase 0 spike report");
            Log($"Started {DateTime.Now:yyyy-MM-dd HH:mm:ss}; machine {Environment.MachineName}; OS {Environment.OSVersion}; " +
                $"CLR {Environment.Version}; 64-bit process {Environment.Is64BitProcess}; session {(SystemParameters.IsRemoteSession ? "remote (RDP/Citrix)" : "console")}");
            Log($"Command line args: [{launch.Raw}]");
            Log($"Exe folder: {AppDomain.CurrentDomain.BaseDirectory}");
            Log($"Report file: {_reportPath}");
            Log("[PASS] STEP 4 - WPF UI window rendered (check visually: Fluent styling, icons, theme). Note anything odd in the checklist.");
            Log($"[INFO] Current theme: {ApplicationThemeManager.GetAppTheme()}");

            Closing += OnClosing;
        }

        // ---------------------------------------------------------------- logging
        private void Log(string message)
        {
            lock (_fileLock)
            {
                try { File.AppendAllText(_reportPath, message + Environment.NewLine); }
                catch { /* the on-screen log is still there */ }
            }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogBox.AppendText(message + Environment.NewLine);
                LogBox.ScrollToEnd();
            }));
        }

        // ---------------------------------------------------------------- inputs
        private SpikeInputs? ReadInputs()
        {
            if (!SpikeInputs.TryParseVector(Iso1Box.Text, out var iso1) || !SpikeInputs.TryParseVector(Iso2Box.Text, out var iso2))
            {
                System.Windows.MessageBox.Show(this, "Isocentre offsets must be three numbers, e.g. 10, 0, 5", "Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            var inp = new SpikeInputs
            {
                PatientId = PatientIdBox.Text.Trim(),
                ClinicalCourseId = ClinicalCourseBox.Text.Trim(),
                ClinicalPlanId = ClinicalPlanBox.Text.Trim(),
                QaCourseId = QaCourseBox.Text.Trim(),
                TemplatePatientId = TemplatePatientBox.Text.Trim(),
                TemplateStudyId = TemplateStudyBox.Text.Trim(),
                TemplateImageId = TemplateImageBox.Text.Trim(),
                PhantomStructureSetId = PhantomSsBox.Text.Trim(),
                BeamId = BeamIdBox.Text.Trim(),
                CopyAllBeams = CopyAllBox.IsChecked == true,
                ForceCopyImage = ForceCopyBox.IsChecked == true,
                SetPrescription = SetRxBox.IsChecked == true,
                SaveAfterWrite = AutoSaveBox.IsChecked == true,
                IsoOffset1 = iso1,
                IsoOffset2 = iso2,
            };
            inp.Save();
            return inp;
        }

        // ---------------------------------------------------------------- step plumbing
        private async Task<T> RunStep<T>(string name, Func<EsapiApplication, SpikeInputs, T> step, T onError)
        {
            var inp = ReadInputs();
            if (inp == null) return onError;

            _worker ??= new EsapiWorker(Log);
            SetBusy(true, name + " (ESAPI calls run on a background thread; dose calculation cannot be cancelled)");
            try
            {
                return await _worker.RunAsync(app => step(app, inp));
            }
            catch (Exception ex)
            {
                Log($"[FAIL] {name} - unhandled exception:\n{ex}");
                return onError;
            }
            finally
            {
                SetBusy(false, "Ready");
            }
        }

        private void SetBusy(bool busy, string status)
        {
            StepButtons.IsEnabled = !busy;
            Busy.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = status;
        }

        private async void Probe_Click(object sender, RoutedEventArgs e) =>
            await RunStep("API probe", (app, _) => _runner.ProbeApi(app, _reportFolder), "");

        private async void Open_Click(object sender, RoutedEventArgs e) =>
            await RunStep("Open patient", (app, inp) => _runner.OpenPatient(app, inp), false);

        private async void Copy_Click(object sender, RoutedEventArgs e)
        {
            var ssId = await RunStep("Copy phantom image", (app, inp) => _runner.CopyPhantom(app, inp), null);
            if (!string.IsNullOrEmpty(ssId)) PhantomSsBox.Text = ssId;
        }

        private async void Inspect_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show(this, "This closes the current patient (unsaved changes are lost), opens the template patient read-only, then re-opens the current patient. Continue?",
                    "Inspect template", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            await RunStep("Inspect template", (app, inp) => _runner.InspectTemplate(app, inp), false);
        }

        private async void Create_Click(object sender, RoutedEventArgs e) =>
            await RunStep("Create QA plan", (app, inp) => _runner.CreateQaPlan(app, inp), null);

        private async void Move_Click(object sender, RoutedEventArgs e) =>
            await RunStep("Move isocentre", (app, inp) => _runner.MoveIso(app, inp), false);

        private async void Save_Click(object sender, RoutedEventArgs e) =>
            await RunStep("Save", (app, _) => { _runner.Save(app); return true; }, false);

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show(this, "Delete all spike QA plans (Comment starts with SRSMC-SPIKE) in the QA course?",
                    "Delete spike plans", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            await RunStep("Delete spike plans", (app, inp) => _runner.DeleteSessionPlans(app, inp), 0);
        }

        private void CopyReport_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(LogBox.Text); StatusText.Text = "Report copied to clipboard. File: " + _reportPath; }
            catch (Exception ex) { StatusText.Text = "Clipboard failed: " + ex.Message + ". File: " + _reportPath; }
        }

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            var next = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
            ApplicationThemeManager.Apply(next);
            Log($"[INFO] Theme switched to {next}.");
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_worker == null) return;
            if (_worker.IsBusy &&
                System.Windows.MessageBox.Show(this, "An ESAPI operation is still running. Closing waits for it to finish (up to 5 minutes). Close anyway?",
                    "Busy", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            Log("[INFO] Closing: disposing ESAPI Application (unsaved changes are discarded).");
            _worker.Dispose();
            _worker = null;
        }
    }
}
