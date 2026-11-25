using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using SrsMcPsqa.App.Infrastructure;
using SrsMcPsqa.App.ViewModels;
using SrsMcPsqa.Core.Services;
using SrsMcPsqa.Esapi;
using SrsMcPsqa.Fake;
using Wpf.Ui.Appearance;

namespace SrsMcPsqa.App
{
    public partial class App : Application
    {
        private IPlanningService? _service;
        private EclipseWarningDialogCloser? _warningCloser;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Dialogs may appear before the main window; don't let closing one of them end the app.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var launch = LaunchOptions.Parse(e.Args);
            var loaded = SettingsStore.Load();
            var settings = loaded.Settings;
            Log.Initialise(settings.LogLevel);
            Log.Info($"Start {Diagnostics.AppVersion}; {launch}; settings from {loaded.Source}");
            foreach (var w in loaded.Warnings) Log.Warn(w);
            Log.Info(Diagnostics.Environment());
            Log.Info(Diagnostics.Settings(settings));

            var dialogs = new DialogService();
            DispatcherUnhandledException += (_, args) =>
            {
                dialogs.ShowError("Unexpected error", args.Exception.Message, args.Exception);
                args.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Error("Unhandled exception", args.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, args) => { Log.Error("Unobserved task exception", args.Exception); args.SetObserved(); };

            SettingsViewModel.ApplyTheme(settings.Theme);

            bool fake = launch.Fake;
            IPlanningService service = fake
                ? new FakePlanningService()
                : new EsapiPlanningService(m => Log.Info("ESAPI: " + m));
            _service = service;
            if (service is EsapiPlanningService esapi) esapi.AutoSave = settings.AutoSave;
            Log.Info(fake ? "Using demo data (--fake)." : "Using Eclipse (ESAPI).");
            // Eclipse opens a message box listing calculation warnings and waits for OK; close warnings-only ones.
            if (!fake) _warningCloser = new EclipseWarningDialogCloser(m => Log.Info(m));

            var session = new PlanningSession(service, settings, launch, dialogs, fake) { SettingsError = loaded.DepartmentError };
            var shell = new ShellViewModel(session, fake);
            var window = new MainWindow { DataContext = shell };
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (settings.Theme == "System") window.Loaded += (_, __) => SystemThemeWatcher.Watch(window);
            window.Show();
            shell.SelectedPage.OnNavigatedTo();
            if (loaded.DepartmentError != null)
                dialogs.ShowError("Department settings could not be read",
                    "The department settings file could not be read, so the app is using its built-in defaults " +
                    "(including the collision check values and the QA course). Creating QA plans is off until the file is fixed." +
                    $"{Environment.NewLine}{Environment.NewLine}{loaded.DepartmentError}{Environment.NewLine}{Environment.NewLine}" +
                    "Fix the file (or ask whoever maintains it), then use Settings → Reload.");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // With auto-save off, save once now; then close the patient and dispose ESAPI on its own thread.
            if (_service is EsapiPlanningService esapi && !esapi.AutoSave)
            {
                try
                {
                    if (esapi.SaveAsync().Wait(TimeSpan.FromMinutes(2))) Log.Info("Saved changes on exit.");
                    else Log.Error("Saving on exit did not finish within 2 minutes; changes may not have been saved.");
                }
                catch (Exception ex) { Log.Error("Saving on exit failed", ex); }
            }
            _warningCloser?.Dispose();
            try { (_service as IDisposable)?.Dispose(); }
            catch (Exception ex) { Log.Error("Closing Eclipse connection", ex); }
            Log.Info("Exit.");
            base.OnExit(e);
        }
    }
}
