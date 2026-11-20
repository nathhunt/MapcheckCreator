using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace SrsMcPsqa.App.Infrastructure
{
    public enum LogLevel { Debug, Info, Warn, Error }

    /// <summary>
    /// Log in a <c>logs</c> folder next to the app (the installation folder), one file per user per computer:
    /// <c>logs\srsmcpsqa_&lt;user&gt;_&lt;computer&gt;.log</c>. When the file passes 10 MB it is renamed to
    /// <c>…_previous.log</c> (replacing the older one) and a new file starts. If the app folder's <c>logs</c> folder
    /// can't be written, the same file goes to <c>%LOCALAPPDATA%\SrsMcPsqa\logs</c> instead (noted in the log).
    /// Only the patient ID is ever logged as a patient identifier.
    /// </summary>
    public static class Log
    {
        private const long MaxBytes = 10L * 1024 * 1024;
        private static readonly object Lock = new object();
        public static LogLevel MinLevel { get; set; } = LogLevel.Info;

        /// <summary>The logs folder next to the app.</summary>
        public static string AppFolder { get; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        /// <summary>Used only when <see cref="AppFolder"/> can't be written.</summary>
        public static string FallbackFolder { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SrsMcPsqa", "logs");

        /// <summary>The folder in use.</summary>
        public static string Folder { get; private set; } = AppFolder;

        public static string FileName { get; } = $"srsmcpsqa_{Safe(Environment.UserName)}_{Safe(Environment.MachineName)}.log";

        public static string CurrentFile => Path.Combine(Folder, FileName);

        private static string Safe(string s)
        {
            var bad = Path.GetInvalidFileNameChars();
            return new string((s ?? "unknown").Select(c => bad.Contains(c) || c == '_' ? '-' : c).ToArray());
        }

        public static void Initialise(string level)
        {
            if (Enum.TryParse(level, true, out LogLevel l)) MinLevel = l;
            lock (Lock)
            {
                Folder = CanWrite(AppFolder) ? AppFolder : FallbackFolder;
                try { Directory.CreateDirectory(Folder); } catch { /* logging must never stop the app */ }
            }
            if (Folder != AppFolder) Warn($"Can't write to {AppFolder}; logging to {Folder} instead.");
        }

        private static bool CanWrite(string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, FileName), "");
                return true;
            }
            catch
            {
                return false;
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
                // The same user may run the app twice on one computer: retry briefly if the other copy holds the file.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var file = CurrentFile;
                        var info = new FileInfo(file);
                        if (info.Exists && info.Length > MaxBytes)
                        {
                            var previous = Path.Combine(Folder, Path.GetFileNameWithoutExtension(FileName) + "_previous.log");
                            if (File.Exists(previous)) File.Delete(previous);
                            File.Move(file, previous);
                        }
                        File.AppendAllText(file, line);
                        return;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(20);
                    }
                    catch
                    {
                        return; // logging must never stop the app
                    }
                }
            }
        }
    }
}
