using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>
    /// Remembers how long dose calculations took, as seconds per control point per computer and calculation model,
    /// so the progress panel can estimate the remaining time. Eclipse doesn't report progress inside a calculation,
    /// so this is the only basis for an estimate. Stored in %LOCALAPPDATA%\SrsMcPsqa\calc-timing.json.
    /// </summary>
    public sealed class CalcTimingStore
    {
        private const double Smoothing = 0.5; // weight of the newest calculation in the running average

        private readonly string _path;
        private readonly Dictionary<string, double> _secondsPerCp;
        private readonly object _lock = new object();

        public CalcTimingStore(string? path = null)
        {
            _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SrsMcPsqa", "calc-timing.json");
            _secondsPerCp = Load(_path);
        }

        public static CalcTimingStore Shared { get; } = new CalcTimingStore();

        private static string Key(string? model) => $"{Environment.MachineName}|{(string.IsNullOrWhiteSpace(model) ? "?" : model)}";

        /// <summary>Estimated duration, or null when this model hasn't been timed on this computer yet.</summary>
        public TimeSpan? Estimate(string? model, int controlPoints)
        {
            if (controlPoints <= 0) return null;
            lock (_lock)
                return _secondsPerCp.TryGetValue(Key(model), out var s) ? TimeSpan.FromSeconds(s * controlPoints) : (TimeSpan?)null;
        }

        public void Record(string? model, int controlPoints, TimeSpan duration)
        {
            if (controlPoints <= 0 || duration <= TimeSpan.Zero) return;
            double s = duration.TotalSeconds / controlPoints;
            lock (_lock)
            {
                string key = Key(model);
                _secondsPerCp[key] = _secondsPerCp.TryGetValue(key, out var old) ? Smoothing * s + (1 - Smoothing) * old : s;
                Save();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var ser = new DataContractJsonSerializer(typeof(TimingFile));
                using var fs = File.Create(_path);
                ser.WriteObject(fs, new TimingFile { SecondsPerControlPoint = new Dictionary<string, double>(_secondsPerCp) });
            }
            catch (Exception ex) { Log.Warn("Could not save calculation timings: " + ex.Message); }
        }

        private static Dictionary<string, double> Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new Dictionary<string, double>();
                var ser = new DataContractJsonSerializer(typeof(TimingFile));
                using var fs = File.OpenRead(path);
                var file = (TimingFile)ser.ReadObject(fs);
                return file.SecondsPerControlPoint != null ? new Dictionary<string, double>(file.SecondsPerControlPoint) : new Dictionary<string, double>();
            }
            catch (Exception ex)
            {
                Log.Warn("Could not read calculation timings (starting afresh): " + ex.Message);
                return new Dictionary<string, double>();
            }
        }

        [DataContract]
        private sealed class TimingFile
        {
            [DataMember] public Dictionary<string, double>? SecondsPerControlPoint { get; set; }
        }
    }
}
