using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using SrsMcPsqa.Core.Optimisation;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>
    /// App settings (JSON). Department defaults come from <c>SrsMcPsqa.settings.json</c> next to the exe (read-only, optional);
    /// a per-user override is stored in <c>%APPDATA%\SrsMcPsqa\settings.json</c>. The app never writes to its own folder.
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public string TemplatePatientId { get; set; } = "";
        [DataMember] public string TemplateStudyId { get; set; } = "";
        [DataMember] public string Template0ImageId { get; set; } = "";
        [DataMember] public string Template270ImageId { get; set; } = "";
        [DataMember] public bool CopyTemplateStructures { get; set; }
        [DataMember] public string PlanNamingPattern { get; set; } = Core.Models.QaPlanNaming.DefaultPattern;
        [DataMember] public double EdgeMarginMm { get; set; } = 3.0;
        [DataMember] public double Tau { get; set; } = 0.85;
        [DataMember] public int MinDiodes { get; set; } = 5;
        [DataMember] public double Alpha { get; set; } = 0.5;
        [DataMember] public double FineTuneInPlaneRangeMm { get; set; } = 15.0;
        [DataMember] public double FineTuneNormalRangeMm { get; set; } = 10.0;
        [DataMember] public bool AutoSave { get; set; } = true;
        [DataMember] public string LogLevel { get; set; } = "Info";
        /// <summary>System, Light or Dark.</summary>
        [DataMember] public string Theme { get; set; } = "System";

        public bool HasTemplate =>
            !string.IsNullOrWhiteSpace(TemplatePatientId) && !string.IsNullOrWhiteSpace(TemplateStudyId)
            && !string.IsNullOrWhiteSpace(Template0ImageId) && !string.IsNullOrWhiteSpace(Template270ImageId);

        public TemplatePhantomSettings ToTemplate() => new TemplatePhantomSettings
        {
            PatientId = TemplatePatientId, StudyId = TemplateStudyId, Image0Id = Template0ImageId,
            Image270Id = Template270ImageId, CopyTemplateStructures = CopyTemplateStructures,
        };

        public OptimisationSettings ToOptimisation() => new OptimisationSettings
        {
            EdgeMarginMm = EdgeMarginMm, Tau = Tau, MinDiodes = MinDiodes, Alpha = Alpha,
            FineTuneInPlaneRangeMm = FineTuneInPlaneRangeMm, FineTuneNormalRangeMm = FineTuneNormalRangeMm,
        };

        public AppSettings Clone() => (AppSettings)MemberwiseClone();

        // DataContract deserialisation skips constructors and initialisers; restore defaults for missing members.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext _)
        {
            var d = new AppSettings(false);
            TemplatePatientId = d.TemplatePatientId; TemplateStudyId = d.TemplateStudyId; Template0ImageId = d.Template0ImageId;
            Template270ImageId = d.Template270ImageId; PlanNamingPattern = d.PlanNamingPattern; EdgeMarginMm = d.EdgeMarginMm;
            Tau = d.Tau; MinDiodes = d.MinDiodes; Alpha = d.Alpha; FineTuneInPlaneRangeMm = d.FineTuneInPlaneRangeMm;
            FineTuneNormalRangeMm = d.FineTuneNormalRangeMm; AutoSave = d.AutoSave; LogLevel = d.LogLevel; Theme = d.Theme;
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

        /// <summary>User file if present, else department file, else built-in defaults. Also says where they came from.</summary>
        public static AppSettings Load(out string source)
        {
            foreach (var (path, label) in new[] { (UserPath, "user settings"), (DepartmentPath, "department defaults") })
            {
                try
                {
                    if (File.Exists(path)) { source = $"{label} ({path})"; return Read(path); }
                }
                catch (Exception ex)
                {
                    Log.Warn($"Could not read {label} from {path}: {ex.Message}");
                }
            }
            source = "built-in defaults";
            return new AppSettings();
        }

        public static AppSettings LoadDepartmentDefaults()
        {
            try { if (File.Exists(DepartmentPath)) return Read(DepartmentPath); }
            catch (Exception ex) { Log.Warn($"Could not read department defaults: {ex.Message}"); }
            return new AppSettings();
        }

        public static void SaveUser(AppSettings s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UserPath)!);
            var ser = new DataContractJsonSerializer(typeof(AppSettings));
            using var ms = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, ownsStream: false, indent: true))
                ser.WriteObject(writer, s);
            File.WriteAllBytes(UserPath, ms.ToArray());
        }

        public static void DeleteUser()
        {
            if (File.Exists(UserPath)) File.Delete(UserPath);
        }

        private static AppSettings Read(string path)
        {
            var ser = new DataContractJsonSerializer(typeof(AppSettings));
            using var fs = File.OpenRead(path);
            return (AppSettings)ser.ReadObject(fs);
        }
    }
}
