using System;
using System.Windows;
using Wpf.Ui.Appearance;

namespace SrsMcPsqa.Spike
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (_, args) =>
            {
                System.Windows.MessageBox.Show(args.Exception.ToString(), "Unhandled error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            var launch = LaunchArgs.Parse(e.Args);

            // Follow the Windows light/dark setting. Spike check: does this work in a Citrix session?
            try { ApplicationThemeManager.ApplySystemTheme(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            var window = new MainWindow(launch);
            MainWindow = window;
            window.Show();
        }
    }

    /// <summary>Command-line arguments from the launcher: --patient, --course, --plan (all optional).</summary>
    public sealed class LaunchArgs
    {
        public string PatientId { get; private set; } = "";
        public string CourseId { get; private set; } = "";
        public string PlanId { get; private set; } = "";
        public string Raw { get; private set; } = "";

        public static LaunchArgs Parse(string[] args)
        {
            var r = new LaunchArgs { Raw = string.Join(" | ", args) };
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--patient": r.PatientId = args[++i]; break;
                    case "--course": r.CourseId = args[++i]; break;
                    case "--plan": r.PlanId = args[++i]; break;
                }
            }
            return r;
        }
    }
}
