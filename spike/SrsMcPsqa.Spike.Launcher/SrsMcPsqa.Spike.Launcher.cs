// SRS MapCHECK PSQA - Phase 0 spike launcher.
// Single-file, READ-ONLY Eclipse plugin script. Copy this file into the Eclipse scripts folder
// (e.g. the published-scripts folder on the Citrix servers) and run it from Tools > Scripts.
// Written in conservative C# (no nullable annotations, no ?. operators) in case Eclipse compiles it with an older language version.
// It starts the standalone spike executable with the current patient/course/plan.
//
// Where the exe is looked for, in order:
//   1. environment variable SRSMCPSQA_SPIKE_EXE
//   2. a text file "SrsMcPsqa.Spike.Launcher.txt" next to this script whose first line is the exe path
//      VERIFY: whether Assembly.Location is usable for a single-file .cs plugin (the report shows what it returns)
//   3. the constant DefaultExePath below. EDIT IT for your site.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using VMS.TPS.Common.Model.API;

namespace VMS.TPS
{
    public class Script
    {
        // EDIT: UNC path of the spike exe on the Citrix servers.
        private const string DefaultExePath = @"\\server\share\SrsMcPsqa\Spike\SrsMcPsqa.Spike.exe";
        private const string ConfigFileName = "SrsMcPsqa.Spike.Launcher.txt";

        public void Execute(ScriptContext context)
        {
            var tried = new StringBuilder();
            string exe = FindExe(tried);
            if (exe == null)
            {
                MessageBox.Show("SrsMcPsqa spike executable not found. Locations tried:\n\n" + tried,
                    "SRS MapCHECK PSQA", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string patientId = context.Patient != null ? context.Patient.Id : "";
            string courseId = context.Course != null ? context.Course.Id : "";
            string planId = context.PlanSetup != null ? context.PlanSetup.Id : "";

            var args = new StringBuilder();
            if (patientId.Length > 0) args.Append("--patient ").Append(Quote(patientId)).Append(' ');
            if (courseId.Length > 0) args.Append("--course ").Append(Quote(courseId)).Append(' ');
            if (planId.Length > 0) args.Append("--plan ").Append(Quote(planId));

            try
            {
                Process.Start(new ProcessStartInfo(exe, args.ToString().Trim()) { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start " + exe + "\n\n" + ex, "SRS MapCHECK PSQA", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            MessageBox.Show(
                "The PSQA spike is starting for patient " + patientId + ".\n\n" +
                "If it reports that it cannot modify the patient, close the patient in Eclipse and click step 1 again.\n" +
                "Re-open the patient in Eclipse afterwards to see the new plans.\n\n" +
                "Launcher diagnostics (please include in the spike report):\n" + tried,
                "SRS MapCHECK PSQA", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static string FindExe(StringBuilder tried)
        {
            string env = Environment.GetEnvironmentVariable("SRSMCPSQA_SPIKE_EXE");
            tried.AppendLine("1. %SRSMCPSQA_SPIKE_EXE% = " + (env != null ? env : "(not set)"));
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

            string location = "";
            try { location = Assembly.GetExecutingAssembly().Location; } catch (Exception ex) { location = "(error: " + ex.Message + ")"; }
            tried.AppendLine("2. Script assembly location = '" + location + "'");
            try
            {
                if (!string.IsNullOrEmpty(location) && File.Exists(location))
                {
                    string cfg = Path.Combine(Path.GetDirectoryName(location), ConfigFileName);
                    tried.AppendLine("   config file = " + cfg + (File.Exists(cfg) ? "" : " (not found)"));
                    if (File.Exists(cfg))
                    {
                        string path = File.ReadAllLines(cfg).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("#"));
                        tried.AppendLine("   config exe = " + (path != null ? path : "(empty)"));
                        if (path != null && File.Exists(path)) return path;
                    }
                }
            }
            catch (Exception ex) { tried.AppendLine("   config read error: " + ex.Message); }

            tried.AppendLine("3. Default = " + DefaultExePath + (File.Exists(DefaultExePath) ? "" : " (not found)"));
            return File.Exists(DefaultExePath) ? DefaultExePath : null;
        }

        /// <summary>Quote one command-line argument for the MSVCRT/.NET argument parser.</summary>
        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in s)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1); }
                else { sb.Append('\\', backslashes); }
                backslashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', backslashes * 2);
            return sb.Append('"').ToString();
        }
    }
}
