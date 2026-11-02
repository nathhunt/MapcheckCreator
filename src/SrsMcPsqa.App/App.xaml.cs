using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using SrsMcPsqa.App.Infrastructure;
using SrsMcPsqa.App.ViewModels;
using SrsMcPsqa.Core.Services;
using SrsMcPsqa.Fake;
using Wpf.Ui.Appearance;

namespace SrsMcPsqa.App
{
    public partial class App : Application
    {
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
            if (!fake)
            {
                dialogs.ShowInfo("Demo data only",
                    "This build (Phase 2) runs on synthetic demo data only; the Eclipse connection is added in Phase 3.\n\nStarting with demo data.");
                fake = true;
            }
            IPlanningService service = new FakePlanningService();

            var session = new PlanningSession(service, settings, launch, dialogs);
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
            Log.Info("Exit.");
            base.OnExit(e);
        }
    }
}
