using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using SrsMcPsqa.Core.Optimisation;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>
    /// App settings (JSON). Everything except theme and log level is a department (site) setting, read from
    /// <c>SrsMcPsqa.settings.json</c> next to the exe (optional; built-in defaults otherwise) and the same for every user of
    /// that installation; Settings can write it back after confirmation (<see cref="SettingsStore.SaveDepartment"/>).
    /// Theme and log level are personal, stored in <c>%APPDATA%\SrsMcPsqa\settings.json</c>.
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public string TemplatePatientId { get; set; } = "";
        /// <summary>Study of the 0° image (empty = search all studies).</summary>
        [DataMember] public string Template0StudyId { get; set; } = "";
        /// <summary>Study of the 270° image (empty = search all studies).</summary>
        [DataMember] public string Template270StudyId { get; set; } = "";
        [DataMember] public string Template0ImageId { get; set; } = "";
        [DataMember] public string Template270ImageId { get; set; } = "";
        [DataMember] public bool CopyTemplateStructures { get; set; }
        /// <summary>Comma-separated patterns of helper PTV structures, listed but unticked by default ("*" = any text).</summary>
        [DataMember] public string HelperPatterns { get; set; } = Core.Prescription.PtvFilter.DefaultHelperPatterns;
        [DataMember] public string PlanNamingPattern { get; set; } = Core.Models.QaPlanNaming.DefaultPattern;
        /// <summary>QA course ID: preselected when it exists, otherwise created with the first QA plan.</summary>
        [DataMember] public string QaCourseId { get; set; } = "PHYS_QA";
        [DataMember] public double EdgeMarginMm { get; set; } = 3.0;
        [DataMember] public double Tau { get; set; } = 0.85;
        [DataMember] public int MinDiodes { get; set; } = 5;
        [DataMember] public double Alpha { get; set; } = 0.5;
        [DataMember] public double FineTuneInPlaneRangeMm { get; set; } = 15.0;
        [DataMember] public double FineTuneNormalRangeMm { get; set; } = 10.0;
        /// <summary>
        /// Shift components smaller than this (mm) are rounded to zero: the QA isocentre shift from the MapCHECK origin in
        /// array planning, and the change in position when fine-tuning. 0 = off.
        /// </summary>
        [DataMember] public double ShiftThresholdMm { get; set; } = 2.0;
        [DataMember] public bool AutoSave { get; set; } = true;

        // Gantry–couch collision check (mm). Head: 41.5 cm iso to face, 76.5 cm across (open-source ESAPI collision scripts);
        // couch: Exact IGRT 53 cm wide, thickness estimated. See docs/COLLISION_CHECK.md.
        [DataMember] public double CollisionHeadClearanceMm { get; set; } = 375;
        [DataMember] public double CollisionHeadRadiusMm { get; set; } = 385;
        [DataMember] public double CollisionCouchWidthMm { get; set; } = 530;
        [DataMember] public double CollisionCouchThicknessMm { get; set; } = 50;
        /// <summary>MapCHECK user origin (array centre) height above the couch top; 0 = not set (check disabled).</summary>
        [DataMember] public double CollisionOriginAboveCouchMm { get; set; } = 200;
        /// <summary>Couch top head end superior of the array centre; −60 = the array centre overhangs the couch end by 60 mm.</summary>
        [DataMember] public double CollisionCouchHeadEndSupMm { get; set; } = -60;
        [DataMember] public double CollisionPhantomLateralOffsetMm { get; set; }
        [DataMember] public double CollisionMaxLateralOffsetMm { get; set; } = 50;
        [DataMember] public double CollisionWarningMm { get; set; } = 30;
        [DataMember] public double CollisionRiskMm { get; set; } = 15;
        [DataMember] public string LogLevel { get; set; } = "Info";
        /// <summary>System, Light or Dark.</summary>
        [DataMember] public string Theme { get; set; } = "System";

        public bool HasTemplate =>
            !string.IsNullOrWhiteSpace(TemplatePatientId)
            && !string.IsNullOrWhiteSpace(Template0ImageId) && !string.IsNullOrWhiteSpace(Template270ImageId);

        public TemplatePhantomSettings ToTemplate() => new TemplatePhantomSettings
        {
            PatientId = TemplatePatientId, Study0Id = Template0StudyId, Study270Id = Template270StudyId, Image0Id = Template0ImageId,
            Image270Id = Template270ImageId, CopyTemplateStructures = CopyTemplateStructures,
        };

        public OptimisationSettings ToOptimisation() => new OptimisationSettings
        {
            EdgeMarginMm = EdgeMarginMm, Tau = Tau, MinDiodes = MinDiodes, Alpha = Alpha,
            FineTuneInPlaneRangeMm = FineTuneInPlaneRangeMm, FineTuneNormalRangeMm = FineTuneNormalRangeMm,
        };

        public Core.Collision.CollisionSettings ToCollision() => new Core.Collision.CollisionSettings
        {
            HeadClearanceMm = CollisionHeadClearanceMm, HeadRadiusMm = CollisionHeadRadiusMm,
            CouchWidthMm = CollisionCouchWidthMm, CouchThicknessMm = CollisionCouchThicknessMm,
            OriginAboveCouchMm = CollisionOriginAboveCouchMm, CouchHeadEndSupMm = CollisionCouchHeadEndSupMm,
            PhantomLateralOffsetMm = CollisionPhantomLateralOffsetMm, MaxLateralOffsetMm = CollisionMaxLateralOffsetMm,
            WarningMm = CollisionWarningMm, RiskMm = CollisionRiskMm,
        };

        public AppSettings Clone() => (AppSettings)MemberwiseClone();

        // DataContract deserialisation skips constructors and initialisers; restore defaults for missing members.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext _)
        {
            var d = new AppSettings(false);
            TemplatePatientId = d.TemplatePatientId; Template0StudyId = d.Template0StudyId; Template270StudyId = d.Template270StudyId; Template0ImageId = d.Template0ImageId;
            Template270ImageId = d.Template270ImageId; HelperPatterns = d.HelperPatterns; PlanNamingPattern = d.PlanNamingPattern; QaCourseId = d.QaCourseId; EdgeMarginMm = d.EdgeMarginMm;
            Tau = d.Tau; MinDiodes = d.MinDiodes; Alpha = d.Alpha; FineTuneInPlaneRangeMm = d.FineTuneInPlaneRangeMm;
            FineTuneNormalRangeMm = d.FineTuneNormalRangeMm; ShiftThresholdMm = d.ShiftThresholdMm; AutoSave = d.AutoSave; LogLevel = d.LogLevel; Theme = d.Theme;
            CollisionHeadClearanceMm = d.CollisionHeadClearanceMm; CollisionHeadRadiusMm = d.CollisionHeadRadiusMm;
            CollisionCouchWidthMm = d.CollisionCouchWidthMm; CollisionCouchThicknessMm = d.CollisionCouchThicknessMm;
            CollisionOriginAboveCouchMm = d.CollisionOriginAboveCouchMm; CollisionCouchHeadEndSupMm = d.CollisionCouchHeadEndSupMm;
            CollisionPhantomLateralOffsetMm = d.CollisionPhantomLateralOffsetMm; CollisionMaxLateralOffsetMm = d.CollisionMaxLateralOffsetMm;
            CollisionWarningMm = d.CollisionWarningMm; CollisionRiskMm = d.CollisionRiskMm;
        }

        public AppSettings() { }
        private AppSettings(bool _) { }
    }

    /// <summary>Loads and saves <see cref="AppSettings"/>.</summary>
    public static class SettingsStore
    {
        public const string DepartmentFileName = "SrsMcPsqa.settings.json";

        public static string DepartmentPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DepartmentFileName);

        public static string UserPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SrsMcPsqa", "settings.json");

        /// <summary>
        /// Site settings (everything except theme and log level) come only from the department file in this installation's
        /// folder (or built-in defaults), the same for every user; only Settings → Save writes it. Personal preferences (theme,
        /// log level) come from the user's file in %APPDATA%.
        /// </summary>
        /// <remarks>
        /// Doesn't log: it runs before the log is set up. The caller logs <see cref="SettingsLoadResult.Warnings"/>.
        /// A site settings file that exists but can't be read gives built-in defaults and
        /// <see cref="SettingsLoadResult.DepartmentError"/>, which the app shows and which turns QA plan creation off.
        /// </remarks>
        public static SettingsLoadResult Load()
        {
            var warnings = new List<string>();
            var result = TryReadDepartment(out var departmentError) ?? new AppSettings();
            if (result.PlanNamingPattern == Core.Models.QaPlanNaming.PreviousDefaultPattern) result.PlanNamingPattern = Core.Models.QaPlanNaming.DefaultPattern;
            string source;
            if (departmentError != null)
            {
                source = $"built-in defaults: the site settings file could not be read ({departmentError})";
                warnings.Add($"Could not read the site settings file: {departmentError}. Using built-in defaults; creating QA plans is off until it is fixed.");
            }
            else source = File.Exists(DepartmentPath) ? $"site settings from {DepartmentPath}" : "built-in defaults (no site settings file)";
            try
            {
                if (File.Exists(UserPath))
                {
                    var p = Read<PersonalSettings>(UserPath);
                    if (!string.IsNullOrWhiteSpace(p?.Theme)) result.Theme = p!.Theme!;
                    if (!string.IsNullOrWhiteSpace(p?.LogLevel)) result.LogLevel = p!.LogLevel!;
                    source += $"; personal preferences from {UserPath}";
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not read personal preferences from {UserPath}: {ex.Message}");
            }
            try
            {
                if (Directory.Exists(Path.GetDirectoryName(UserPath)!) && Directory.GetFiles(Path.GetDirectoryName(UserPath)!, "template-*.json").Length > 0)
                    source += "; earlier per-user template phantom files are no longer used (site settings file instead)";
            }
            catch (Exception ex) { warnings.Add($"Could not list {Path.GetDirectoryName(UserPath)}: {ex.Message}"); }
            return new SettingsLoadResult(result, source, departmentError, warnings);
        }

        /// <summary>Folder of this installation (test and production installations differ).</summary>
        public static string InstallationFolder => Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd('\\', '/');

        public static AppSettings LoadDepartmentDefaults()
        {
            var s = TryReadDepartment(out var error);
            if (error != null) Log.Warn($"Could not read the site settings file: {error}");
            return s ?? new AppSettings();
        }

        /// <summary>The site settings file; null when there is none or it can't be read (then <paramref name="error"/> says why).</summary>
        private static AppSettings? TryReadDepartment(out string? error)
        {
            error = null;
            if (!File.Exists(DepartmentPath)) return null;
            try
            {
                return Read<AppSettings>(DepartmentPath) ?? throw new InvalidDataException("the file holds no settings (null)");
            }
            catch (Exception ex)
            {
                error = $"{DepartmentPath}: {ex.Message}";
                return null;
            }
        }

        /// <summary>Personal preferences (per user); every other setting is a site (department) setting.</summary>
        public static readonly string[] PersonalFields = { nameof(AppSettings.Theme), nameof(AppSettings.LogLevel) };

        /// <summary>Last-write time of the site settings file when it was read (to detect someone else's change).</summary>
        public static DateTime? DepartmentFileTimeUtc => File.Exists(DepartmentPath) ? File.GetLastWriteTimeUtc(DepartmentPath) : (DateTime?)null;

        /// <summary>Site settings that differ between two settings objects: (field, old, new).</summary>
        public static List<(string Field, string Old, string New)> SiteDifferences(AppSettings old, AppSettings @new)
        {
            var list = new List<(string, string, string)>();
            foreach (var p in typeof(AppSettings).GetProperties())
            {
                if (!p.IsDefined(typeof(DataMemberAttribute), false) || Array.IndexOf(PersonalFields, p.Name) >= 0) continue;
                string a = Convert.ToString(p.GetValue(old), System.Globalization.CultureInfo.InvariantCulture) ?? "";
                string b = Convert.ToString(p.GetValue(@new), System.Globalization.CultureInfo.InvariantCulture) ?? "";
                if (a != b) list.Add((p.Name, a, b));
            }
            return list;
        }

        /// <summary>
        /// Writes the site settings to the department file in the app folder (applies to everyone using this installation).
        /// The personal fields keep the file's own defaults. Needs write access to the app folder.
        /// </summary>
        public static void SaveDepartment(AppSettings settings)
        {
            var current = LoadDepartmentDefaults();
            var s = settings.Clone();
            s.Theme = current.Theme; s.LogLevel = current.LogLevel;
            string tmp = DepartmentPath + ".tmp";
            Write(tmp, s);
            if (File.Exists(DepartmentPath))
            {
                File.Copy(DepartmentPath, DepartmentPath + ".bak", overwrite: true); // previous version, for undo
                File.Delete(DepartmentPath);
            }
            File.Move(tmp, DepartmentPath);
            Log.Info($"Site settings saved to {DepartmentPath} by {Environment.UserName}.");
        }

        /// <summary>Saves the personal preferences (theme, log level) only.</summary>
        public static void SavePersonal(AppSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserPath)!);
            Write(UserPath, new PersonalSettings { Theme = settings.Theme, LogLevel = settings.LogLevel });
        }

        private static void Write<T>(string path, T value)
        {
            var ser = new DataContractJsonSerializer(typeof(T));
            using var ms = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, ownsStream: false, indent: true))
                ser.WriteObject(writer, value);
            File.WriteAllBytes(path, ms.ToArray());
        }

        /// <summary>Deletes the personal preferences.</summary>
        public static void DeletePersonal()
        {
            if (File.Exists(UserPath)) File.Delete(UserPath);
        }

        private static T? Read<T>(string path) where T : class
        {
            string text = File.ReadAllText(path);
            CheckJsonShape(text);
            var ser = new DataContractJsonSerializer(typeof(T));
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return (T?)ser.ReadObject(ms);
        }

        /// <summary>
        /// The serialiser accepts a truncated file (e.g. an interrupted copy) or a trailing comma without complaint (seen
        /// on .NET 8) and fills everything missing with defaults. Refuse those: the file must be one complete { … } object.
        /// </summary>
        internal static void CheckJsonShape(string text)
        {
            var t = text.Trim();
            if (t.Length == 0) throw new InvalidDataException("the file is empty");
            if (t == "null") return; // handled by the caller
            if (t[0] != '{' || t[t.Length - 1] != '}') throw new InvalidDataException("the file is not one complete { … } JSON object (truncated?)");
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @",\s*\}$")) throw new InvalidDataException("there is a comma after the last setting");
        }
    }
}

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>What <see cref="SettingsStore.Load"/> read, where from, and what went wrong.</summary>
    public sealed class SettingsLoadResult
    {
        public SettingsLoadResult(AppSettings settings, string source, string? departmentError, IReadOnlyList<string> warnings)
        {
            Settings = settings; Source = source; DepartmentError = departmentError; Warnings = warnings;
        }

        public AppSettings Settings { get; }
        /// <summary>Where the settings came from, for the log.</summary>
        public string Source { get; }
        /// <summary>The site settings file exists but couldn't be read (path and reason); null when it was read or is absent.</summary>
        public string? DepartmentError { get; }
        /// <summary>Problems to log once the log is set up.</summary>
        public IReadOnlyList<string> Warnings { get; }
    }

    /// <summary>A user's personal preferences (everything else is a site setting).</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class PersonalSettings
    {
        [System.Runtime.Serialization.DataMember] public string? Theme { get; set; }
        [System.Runtime.Serialization.DataMember] public string? LogLevel { get; set; }
    }
}
