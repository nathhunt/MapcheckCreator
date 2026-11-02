using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SrsMcPsqa.App.Infrastructure
{
    public enum LogLevel { Debug, Info, Warn, Error }

    /// <summary>
    /// Local rolling log in <c>%LOCALAPPDATA%\SrsMcPsqa\logs</c>, one file per day, kept for 30 days.
    /// Only the patient ID is ever logged as a patient identifier.
    /// </summary>
    public static class Log
    {
        private static readonly object Lock = new object();
        public static LogLevel MinLevel { get; set; } = LogLevel.Info;

        public static string Folder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SrsMcPsqa", "logs");

        public static string CurrentFile => Path.Combine(Folder, $"srsmcpsqa-{DateTime.Now:yyyyMMdd}.log");

        public static void Initialise(string level)
        {
            if (Enum.TryParse(level, true, out LogLevel l)) MinLevel = l;
            try
            {
                Directory.CreateDirectory(Folder);
                foreach (var f in Directory.GetFiles(Folder, "srsmcpsqa-*.log").Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30)))
                    File.Delete(f);
            }
            catch
            {
                // Logging must never stop the app.
            }
        }

        public static void Debug(string m) => Write(LogLevel.Debug, m);
        public static void Info(string m) => Write(LogLevel.Info, m);
        public static void Warn(string m) => Write(LogLevel.Warn, m);
        public static void Error(string m, Exception? ex = null) => Write(LogLevel.Error, ex == null ? m : m + Environment.NewLine + ex);

        private static void Write(LogLevel level, string message)
        {
            if (level < MinLevel) return;
            var line = string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss.fff} [{1,-5}] {2}{3}",
                DateTime.Now, level.ToString().ToUpperInvariant(), message, Environment.NewLine);
            lock (Lock)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    File.AppendAllText(CurrentFile, line);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}
