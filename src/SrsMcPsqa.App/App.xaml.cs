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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Dialogs may appear before the main window; don't let closing one of them end the app.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var launch = LaunchOptions.Parse(e.Args);
            var settings = SettingsStore.Load(out var source);
            Log.Initialise(settings.LogLevel);
            Log.Info($"Start {Assembly.GetExecutingAssembly().GetName().Version}; {launch}; settings from {source}");

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
            Log.Info(fake ? "Using demo data (--fake)." : "Using Eclipse (ESAPI).");

            var session = new PlanningSession(service, settings, launch, dialogs, fake);
            var shell = new ShellViewModel(session, fake);
            var window = new MainWindow { DataContext = shell };
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (settings.Theme == "System") window.Loaded += (_, __) => SystemThemeWatcher.Watch(window);
            window.Show();
            shell.SelectedPage.OnNavigatedTo();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Closes the patient and disposes the ESAPI Application on its own thread (ends the session).
            try { (_service as IDisposable)?.Dispose(); }
            catch (Exception ex) { Log.Error("Closing Eclipse connection", ex); }
            Log.Info("Exit.");
            base.OnExit(e);
        }
    }
}
